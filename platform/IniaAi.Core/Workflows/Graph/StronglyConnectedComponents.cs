namespace IniaAi.Core.Workflows.Graph;

/// <summary>Tarjan's strongly connected components, iterative so a long chain cannot overflow the stack.</summary>
internal static class StronglyConnectedComponents
{
    /// <summary>
    /// Every component reachable from <paramref name="nodes"/>, each listed once, a node on no
    /// cycle as a component of its own. A successor that is not a key of
    /// <paramref name="successors"/> is ignored.
    /// </summary>
    public static List<List<T>> Find<T>(IEnumerable<T> nodes, IReadOnlyDictionary<T, List<T>> successors)
        where T : notnull
    {
        var tarjan = new Tarjan<T>(successors);
        foreach (T node in nodes)
        {
            tarjan.Visit(node);
        }

        return tarjan.Components;
    }

    private sealed class Tarjan<T>(IReadOnlyDictionary<T, List<T>> successors)
        where T : notnull
    {
        private readonly Dictionary<T, int> _index = [];
        private readonly Dictionary<T, int> _lowLink = [];
        private readonly Stack<T> _stack = new();
        private readonly HashSet<T> _onStack = [];
        private int _counter;

        public List<List<T>> Components { get; } = [];

        public void Visit(T root)
        {
            if (_index.ContainsKey(root))
            {
                return;
            }

            Stack<(T Node, int NextSuccessor)> work = new();
            Open(root);
            work.Push((root, 0));

            while (work.Count > 0)
            {
                (T node, int nextSuccessor) = work.Pop();
                List<T> next = successors.GetValueOrDefault(node) ?? [];

                if (nextSuccessor < next.Count)
                {
                    work.Push((node, nextSuccessor + 1));
                    T successor = next[nextSuccessor];

                    if (!successors.ContainsKey(successor))
                    {
                        continue;
                    }

                    if (!_index.ContainsKey(successor))
                    {
                        Open(successor);
                        work.Push((successor, 0));
                    }
                    else if (_onStack.Contains(successor))
                    {
                        _lowLink[node] = Math.Min(_lowLink[node], _index[successor]);
                    }

                    continue;
                }

                if (_lowLink[node] == _index[node])
                {
                    List<T> component = [];
                    T member;
                    do
                    {
                        member = _stack.Pop();
                        _ = _onStack.Remove(member);
                        component.Add(member);
                    }
                    while (!EqualityComparer<T>.Default.Equals(member, node));

                    Components.Add(component);
                }

                if (work.Count > 0)
                {
                    T parent = work.Peek().Node;
                    _lowLink[parent] = Math.Min(_lowLink[parent], _lowLink[node]);
                }
            }
        }

        private void Open(T node)
        {
            _index[node] = _counter;
            _lowLink[node] = _counter;
            _counter++;
            _stack.Push(node);
            _ = _onStack.Add(node);
        }
    }
}
