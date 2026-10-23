using InertiaNet.Pathfinder.Analysis;

namespace InertiaNet.Pathfinder.Generation;

internal static class ModulePaths
{
    public static Dictionary<string, string> Actions(IEnumerable<string> controllers)
    {
        var items = controllers.Distinct(StringComparer.Ordinal).Select(identity => (Identity: identity,
            Preferred: NameUtils.ControllerToPath(identity == "__MinimalApi__" ? "MinimalApi" : identity)))
            .OrderBy(item => item.Identity, StringComparer.Ordinal).ToArray();
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            var candidate = item.Preferred;
            var leaf = candidate.Split('/')[^1];
            if (leaf == "index" || items.Count(other => other.Preferred.Equals(candidate, StringComparison.OrdinalIgnoreCase)) > 1)
                candidate += "_" + ContractNames.Hash(item.Identity);
            var suffix = 0;
            var initial = candidate;
            while (!used.Add(candidate)) candidate = initial + "_" + ++suffix;
            result[item.Identity] = candidate;
        }
        return result;
    }

    public static Dictionary<string, string> RouteDirectories(IEnumerable<string> names)
    {
        var prefixes = names.SelectMany(name => Enumerable.Range(0, name.Split('.').Length)
            .Select(length => string.Join('.', name.Split('.')[..length]))).Distinct(StringComparer.Ordinal)
            .OrderBy(value => value.Count(character => character == '.')).ThenBy(value => value.Length == 0 ? 0 : 1)
            .ThenBy(value => value, StringComparer.Ordinal).ToArray();
        var result = new Dictionary<string, string>(StringComparer.Ordinal) { [""] = "" };
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var prefix in prefixes.Where(value => value.Length > 0))
        {
            var split = prefix.LastIndexOf('.');
            var parent = split < 0 ? "" : prefix[..split];
            var leaf = ContractNames.Identifier(prefix[(split + 1)..]);
            var path = result[parent].Length == 0 ? leaf : result[parent] + "/" + leaf;
            var candidate = path;
            if (used.Contains(candidate)) candidate += "_" + ContractNames.Hash(prefix);
            var suffix = 0;
            while (!used.Add(candidate)) candidate = path + "_" + ContractNames.Hash(prefix) + "_" + ++suffix;
            result[prefix] = candidate;
        }
        return result;
    }
}
