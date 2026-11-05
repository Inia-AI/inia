using System.Linq.Expressions;
using System.Reflection;

namespace IniaAi.Domain.Specifications.Common;

/// <summary>
/// Composes projection expressions so a derived read model extends its base's projection
/// instead of restating it: one binding list per column, and a column added to the base reaches
/// every derived projection by construction. The result is an ordinary member-init expression
/// the query provider translates like a hand-written one.
/// </summary>
public static class ProjectionExpressions
{
    private static readonly MethodInfo _inline = typeof(ProjectionExpressions).GetMethod(nameof(Inline))!;

    /// <summary>
    /// A member the extension also binds keeps the extension's binding, so a derived type with
    /// <c>required</c> members can restate them without the base binding them twice.
    /// </summary>
    public static Expression<Func<TSource, TDerived>> Extend<TSource, TBase, TDerived>(
        Expression<Func<TSource, TBase>> baseProjection,
        Expression<Func<TSource, TDerived>> extension)
        where TDerived : TBase
    {
        ParameterExpression parameter = extension.Parameters[0];
        MemberInitExpression baseInit = (MemberInitExpression)new ParameterReplacer(baseProjection.Parameters[0], parameter).Visit(baseProjection.Body);
        MemberInitExpression extensionInit = (MemberInitExpression)extension.Body;
        HashSet<string> extended = [.. extensionInit.Bindings.Select(binding => binding.Member.Name)];

        return Expression.Lambda<Func<TSource, TDerived>>(
            Expression.MemberInit(
                extensionInit.NewExpression,
                [.. baseInit.Bindings.Where(binding => !extended.Contains(binding.Member.Name)), .. extensionInit.Bindings]),
            parameter);
    }

    /// <summary>
    /// Builds a projection the query provider can run from one that reuses a stored rule: the
    /// projection calls <see cref="Inline{TArgument, TResult}"/> on the rule's expression, and the
    /// rule's body takes the call's place, so the rule has one source.
    /// </summary>
    public static Expression<Func<TSource, TResult>> Of<TSource, TResult>(Expression<Func<TSource, TResult>> projection)
    {
        return (Expression<Func<TSource, TResult>>)new InliningVisitor().Visit(projection);
    }

    public static TResult Inline<TArgument, TResult>(this Expression<Func<TArgument, TResult>> rule, TArgument argument)
    {
        throw new InvalidOperationException($"{nameof(Inline)} runs only inside a projection built by {nameof(ProjectionExpressions)}.{nameof(Of)}.");
    }

    private sealed class InliningVisitor : ExpressionVisitor
    {
        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (!node.Method.IsGenericMethod || node.Method.GetGenericMethodDefinition() != _inline)
            {
                return base.VisitMethodCall(node);
            }

            var rule = (LambdaExpression)Expression.Lambda(node.Arguments[0]).Compile().DynamicInvoke()!;
            Expression argument = Visit(node.Arguments[1]);
            return Visit(new ParameterReplacer(rule.Parameters[0], argument).Visit(rule.Body));
        }
    }

    private sealed class ParameterReplacer(ParameterExpression from, Expression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node)
        {
            return node == from ? to : base.VisitParameter(node);
        }
    }
}
