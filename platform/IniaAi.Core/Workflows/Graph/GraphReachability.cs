namespace IniaAi.Core.Workflows.Graph;

internal static class GraphReachability
{
    /// <summary>Whether a path leads from <paramref name="fromId"/> to <paramref name="toId"/>. A node with no entry has no successors.</summary>
    public static bool Reaches(IReadOnlyDictionary<string, List<string>> successors, string fromId, string toId)
    {
        HashSet<string> seen = new(StringComparer.Ordinal) { fromId };
        Queue<string> pending = new([fromId]);

        while (pending.Count > 0)
        {
            string current = pending.Dequeue();
            if (current == toId)
            {
                return true;
            }

            if (!successors.TryGetValue(current, out List<string>? next))
            {
                continue;
            }

            foreach (string successor in next)
            {
                if (seen.Add(successor))
                {
                    pending.Enqueue(successor);
                }
            }
        }

        return false;
    }
}
