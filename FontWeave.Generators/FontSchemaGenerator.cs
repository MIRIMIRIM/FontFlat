using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace FontWeave.Generators;

[Generator(LanguageNames.CSharp)]
public sealed class FontSchemaGenerator : IIncrementalGenerator
{
    internal static readonly DiagnosticDescriptor Shape = Rule("FWG001", "Invalid schema declaration");
    internal static readonly DiagnosticDescriptor Reference = Rule("FWG002", "Invalid schema name or reference");
    internal static readonly DiagnosticDescriptor Layout = Rule("FWG003", "Invalid wire layout or value");
    internal static readonly DiagnosticDescriptor Cycle = Rule("FWG004", "Cyclic schema expression");
    internal static readonly DiagnosticDescriptor Condition = Rule("FWG005", "Invalid schema condition or dependency path");
    internal static readonly DiagnosticDescriptor Hook = Rule("FWG006", "Invalid computed hook binding");
    private static DiagnosticDescriptor Rule(string id, string title) =>
        new(id, title, "{0}", "FontWeave.Schema", DiagnosticSeverity.Error, isEnabledByDefault: true);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterPostInitializationOutput(static output =>
            output.AddSource("FontWeave.Schema.Attributes.g.cs", SourceText.From(SchemaAttributes.Source, Encoding.UTF8)));
        var parsed = context.SyntaxProvider.ForAttributeWithMetadataName(
            "FontWeave.Schema.BinaryTableAttribute",
            static (node, _) => node is TypeDeclarationSyntax,
            static (input, cancellation) => SchemaParser.Parse(input, cancellation));
        context.RegisterSourceOutput(parsed, static (output, result) =>
        {
            foreach (var diagnostic in result.Diagnostics) output.ReportDiagnostic(diagnostic);
        });
        var schemas = parsed.Where(static result => result.Model is not null)
            .Select(static (result, _) => result.Model!)
            .WithComparer(TableEquality.Instance).WithTrackingName("ValidatedSchemas");
        // Spec 8.2 target registry: resolve every reference's typeof target against the
        // same round's BinaryTable models, fold the target contract summary into the
        // referrer identity and refuse reference cycles (card 1.2-g, OQ-5).
        var resolved = schemas.Collect()
            .Select(static (models, cancellation) => TargetRegistry.Resolve(models, cancellation))
            .WithComparer(ResolvedEquality.Instance).WithTrackingName("ResolvedTargets");
        context.RegisterSourceOutput(resolved, static (output, resolved) =>
        {
            foreach (var diagnostic in resolved.Diagnostics) output.ReportDiagnostic(diagnostic);
        });
        var emitted = schemas.Combine(resolved).Select(static (pair, cancellation) =>
        {
            cancellation.ThrowIfCancellationRequested();
            var (model, suppressed) = pair.Right.Resolve(pair.Left);
            // The folded identity rides with the output so a target contract change marks
            // the referrer's emission step Modified even when its emitted text is stable.
            return (model.HintName, Identity: model.Identity, Text: suppressed ? null : ViewEmitter.Emit(model));
        }).WithTrackingName("EmittedTables");
        context.RegisterSourceOutput(emitted, static (output, source) =>
        {
            if (source.Text is { } text) output.AddSource(source.HintName, SourceText.From(text, Encoding.UTF8));
        });
    }

    // The combine-step result: per-referrer resolved models (identity folded with target
    // summaries), per-referrer suppression for unresolvable targets and cycles, and the
    // registry diagnostics. Compared through a canonical string so unrelated table edits
    // leave the value — and every downstream emission node — cached.
    internal sealed class ResolvedSchemas
    {
        internal ResolvedSchemas(ImmutableArray<(string FullName, TableModel Model, bool Suppressed)> resolutions,
            ImmutableArray<Diagnostic> diagnostics, string canonical)
        { Resolutions = resolutions; Diagnostics = diagnostics; Canonical = canonical; }

        internal ImmutableArray<(string FullName, TableModel Model, bool Suppressed)> Resolutions { get; }
        internal ImmutableArray<Diagnostic> Diagnostics { get; }
        internal string Canonical { get; }

        internal (TableModel Model, bool Suppressed) Resolve(TableModel model)
        {
            string fullName = model.Namespace.Length == 0 ? model.Name : model.Namespace + "." + model.Name;
            foreach (var entry in Resolutions)
                if (entry.FullName == fullName)
                    return (entry.Model, entry.Suppressed);
            return (model, false);
        }
    }

    internal sealed class TargetRegistry
    {
        internal static ResolvedSchemas Resolve(ImmutableArray<TableModel> models, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            var byName = new Dictionary<string, TableModel>(StringComparer.Ordinal);
            foreach (var model in models)
                byName[model.Namespace.Length == 0 ? model.Name : model.Namespace + "." + model.Name] = model;
            var diagnostics = new List<Diagnostic>();
            var edges = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            // The conservative Follow graph (card 1.2-c, OQ-3): only Follow edges of
            // scan-enabled contract-version-1 tables participate, and version/selector/role
            // conditions never shrink the build-time graph (every declared edge enters).
            var followEdges = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            var unresolved = new HashSet<string>(StringComparer.Ordinal);
            foreach (var model in models)
            {
                if (model.References.IsEmpty) continue;
                string modelName = model.Namespace.Length == 0 ? model.Name : model.Namespace + "." + model.Name;
                var targets = new List<string>();
                bool failed = false;
                foreach (var reference in model.References)
                {
                    if (reference.TargetFullName is not { } targetName) continue;
                    if (!byName.TryGetValue(targetName, out var target))
                    {
                        diagnostics.Add(Diagnostic.Create(Reference, Location.None,
                            "Unknown reference target schema: " + targetName));
                        failed = true;
                        continue;
                    }
                    if (reference.AllowTargetAtEnd && target.MinimumHeader != 0)
                    {
                        diagnostics.Add(Diagnostic.Create(Layout, Location.None,
                            "AllowTargetAtEnd requires a target with a zero minimum header: " + targetName));
                        failed = true;
                    }
                    // Card 1.2-e (spec 4, FWG005): the target-role binding contract. A
                    // role-bearing target demands exactly one of TargetRole or ForwardRole;
                    // a roleless target carries neither; the fixed role must be supported by
                    // the target; forwarding demands the same RoleType and — conservatively
                    // per OQ-3 — the whole source SupportedRoles set inside the target's.
                    if (reference.TargetRole != long.MinValue || reference.ForwardRole)
                    {
                        if (target.RoleType is null)
                        {
                            diagnostics.Add(Diagnostic.Create(Condition, Location.None,
                                "A roleless target cannot carry TargetRole or ForwardRole: " + reference.Name));
                            failed = true;
                        }
                        else if (reference.ForwardRole)
                        {
                            if (reference.TargetRole != long.MinValue)
                            {
                                diagnostics.Add(Diagnostic.Create(Condition, Location.None,
                                    "A reference specifies at most one of TargetRole and ForwardRole: " + reference.Name));
                                failed = true;
                            }
                            else if (target.RoleType != model.RoleType)
                            {
                                diagnostics.Add(Diagnostic.Create(Condition, Location.None,
                                    "ForwardRole requires the same RoleType on both sides: " + reference.Name));
                                failed = true;
                            }
                            else if (!model.SupportedRoles.All(role => target.SupportedRoles.Contains(role)))
                            {
                                diagnostics.Add(Diagnostic.Create(Condition, Location.None,
                                    "ForwardRole requires the source covered roles inside the target support: " + reference.Name));
                                failed = true;
                            }
                        }
                        else if (!target.SupportedRoles.Contains(reference.TargetRole))
                        {
                            diagnostics.Add(Diagnostic.Create(Condition, Location.None,
                                "TargetRole is not a supported role of the target: " + reference.Name));
                            failed = true;
                        }
                    }
                    else if (target.RoleType is not null)
                    {
                        diagnostics.Add(Diagnostic.Create(Condition, Location.None,
                            "A reference to a role-bearing target specifies exactly one of TargetRole or ForwardRole: " + reference.Name));
                        failed = true;
                    }
                    // Card 1.2-c (spec 6.1, FWG005): a Follow target must itself opt into
                    // the contract-version-1 structure scan ("the target may be V0, Follow
                    // may not"); only these declared edges join the conservative DAG.
                    if (reference.Scan == ReferenceScan.Follow)
                    {
                        if (target.ContractVersion != 1 || !target.GenerateStructureScan)
                        {
                            diagnostics.Add(Diagnostic.Create(Condition, Location.None,
                                "A Follow reference requires the target to enable GenerateStructureScan under contract version 1: " + reference.Name));
                            failed = true;
                        }
                        else
                        {
                            if (!followEdges.TryGetValue(modelName, out var followTargets))
                                followEdges[modelName] = followTargets = new List<string>();
                            followTargets.Add(targetName);
                        }
                    }
                    targets.Add(targetName);
                }
                if (failed)
                {
                    // An unresolved or inconsistent reference is a build error; the stale
                    // output of the referrer must not survive (spec 8.1).
                    diagnostics.Add(Diagnostic.Create(Reference, Location.None,
                        "The references of " + modelName + " did not resolve; its stale output is cleared."));
                    unresolved.Add(modelName);
                    continue;
                }
                if (targets.Count != 0) edges[modelName] = targets;
            }
            // Reference identity must terminate (card OQ-5): a referrer->target cycle would
            // make the folded canonical identity recursive, so it is refused at build time.
            var cyclic = new HashSet<string>(StringComparer.Ordinal);
            var visiting = new HashSet<string>(StringComparer.Ordinal);
            var visited = new HashSet<string>(StringComparer.Ordinal);
            foreach (var start in edges.Keys)
                if (Visit(start, edges, visiting, visited, cyclic, out var cyclePath))
                    diagnostics.Add(Diagnostic.Create(Cycle, Location.None,
                        "Reference target identity cycle: " + string.Join(" -> ", cyclePath)));
            // Card 1.2-c (spec 6.1, FWG005): the maximum static Follow depth is 64 nodes
            // including the root; deeper chains are refused at build time. Follow cycles
            // need no extra check — every reference cycle is already terminated by the
            // identity rule above (card OQ-5, registered coverage), so the memoized walk
            // over the declared DAG terminates. The DAG summary is deliberately not folded
            // into any identity: the graph is uniquely determined by the Follow
            // declarations, which are already identity inputs.
            var tooDeep = new HashSet<string>(StringComparer.Ordinal);
            var followDepth = new Dictionary<string, int>(StringComparer.Ordinal);
            var followStack = new HashSet<string>(StringComparer.Ordinal);
            int DepthOf(string node)
            {
                cancellation.ThrowIfCancellationRequested();
                if (followDepth.TryGetValue(node, out int cached)) return cached;
                if (followStack.Contains(node)) return 0;
                followStack.Add(node);
                int depth = 1;
                if (followEdges.TryGetValue(node, out var targets))
                    foreach (var target in targets)
                        depth = Math.Max(depth, 1 + DepthOf(target));
                followStack.Remove(node);
                followDepth[node] = depth;
                return depth;
            }
            foreach (var model in models)
            {
                if (model.ContractVersion != 1 || !model.GenerateStructureScan) continue;
                string fullName = model.Namespace.Length == 0 ? model.Name : model.Namespace + "." + model.Name;
                if (DepthOf(fullName) > 64)
                {
                    diagnostics.Add(Diagnostic.Create(Condition, Location.None,
                        "The Follow chain of " + fullName + " exceeds the maximum static depth of 64 nodes."));
                    tooDeep.Add(fullName);
                }
            }
            var resolutions = ImmutableArray.CreateBuilder<(string FullName, TableModel Model, bool Suppressed)>();
            var canonical = new StringBuilder();
            foreach (var model in models)
            {
                if (model.References.IsEmpty) continue;
                cancellation.ThrowIfCancellationRequested();
                string fullName = model.Namespace.Length == 0 ? model.Name : model.Namespace + "." + model.Name;
                bool suppressed = unresolved.Contains(fullName) || cyclic.Contains(fullName) || tooDeep.Contains(fullName);
                var folded = new StringBuilder();
                var references = model.References;
                for (int i = 0; i < references.Length; i++)
                {
                    var updated = Clone(references[i]);
                    if (updated.TargetFullName is null)
                    {
                        // A targetless reference declares no typed opener (spec 3.3); it is
                        // legal and participates in no registry resolution.
                        updated.TargetMinimumHeader = model.MinimumHeader;
                    }
                    else if (byName.TryGetValue(updated.TargetFullName, out var target))
                    {
                        updated.TargetMinimumHeader = target.MinimumHeader;
                        updated.TargetNamespace = target.Namespace;
                        updated.TargetName = target.Name;
                        updated.TargetIdentity = target.Identity;
                        updated.TargetRoleType = target.RoleType;
                        folded.Append('|').Append(references[i].Name).Append('@').Append(target.Identity);
                    }
                    else
                        suppressed = true;
                    references = references.SetItem(i, updated);
                }
                if (cyclic.Contains(fullName))
                {
                    suppressed = true;
                    folded.Clear().Append("|cycle");
                }
                var resolvedModel = Clone(model, references);
                resolvedModel.Identity = model.Identity + "#refs" + folded;
                resolutions.Add((fullName, resolvedModel, suppressed));
                canonical.Append(fullName).Append('#').Append(suppressed ? '!' : ' ').Append(resolvedModel.Identity).Append(';');
            }
            canonical.Append('#').Append(diagnostics.Count);
            foreach (var diagnostic in diagnostics)
                canonical.Append(';').Append(diagnostic.Id).Append(':').Append(diagnostic.GetMessage());
            return new ResolvedSchemas(resolutions.ToImmutable(), diagnostics.ToImmutableArray(), canonical.ToString());
        }

        private static bool Visit(string start, Dictionary<string, List<string>> edges,
            HashSet<string> visiting, HashSet<string> visited, HashSet<string> cyclic, out List<string> cyclePath)
        {
            cyclePath = new List<string>();
            if (cyclic.Contains(start)) return false;
            var stack = new List<(string Node, int Edge)>();
            var path = new List<string>();
            visiting.Clear();
            path.Clear();
            stack.Add((start, 0));
            visiting.Add(start);
            path.Add(start);
            while (stack.Count != 0)
            {
                int last = stack.Count - 1;
                var (node, edgeIndex) = stack[last];
                if (edgeIndex >= (edges.TryGetValue(node, out var targets) ? targets.Count : 0))
                {
                    stack.RemoveAt(last);
                    visiting.Remove(node);
                    path.RemoveAt(path.Count - 1);
                    visited.Add(node);
                    continue;
                }
                stack[last] = (node, edgeIndex + 1);
                string next = edges[node][edgeIndex];                if (visiting.Contains(next))
                {
                    cyclePath.AddRange(path);
                    cyclePath.Add(next);
                    int from = path.IndexOf(next);
                    for (int i = 0; i < from && cyclePath.Count != 0; i++) cyclePath.RemoveAt(0);
                    foreach (var cyclicNode in cyclePath) cyclic.Add(cyclicNode);
                    return true;
                }
                if (visited.Contains(next)) continue;
                visiting.Add(next);
                path.Add(next);
                stack.Add((next, 0));
            }
            return false;
        }

        private static ReferenceModel Clone(ReferenceModel reference) => new()
        {
            Name = reference.Name, OffsetField = reference.OffsetField, ArrayName = reference.ArrayName,
            TargetFullName = reference.TargetFullName, Origin = reference.Origin, Base = reference.Base,
            Minimum = reference.Minimum, Length = reference.Length, NullPolicy = reference.NullPolicy,
            ZeroFailure = reference.ZeroFailure, AllowTargetAtEnd = reference.AllowTargetAtEnd,
            WhenRoles = reference.WhenRoles, Selector = reference.Selector, SelectorValues = reference.SelectorValues,
            DefaultRaw = reference.DefaultRaw, TargetRole = reference.TargetRole, ForwardRole = reference.ForwardRole,
            Scan = reference.Scan, ScanOrder = reference.ScanOrder, WhenVersions = reference.WhenVersions,
            TargetMinimumHeader = reference.TargetMinimumHeader, TargetNamespace = reference.TargetNamespace,
            TargetName = reference.TargetName, TargetIdentity = reference.TargetIdentity,
            TargetRoleType = reference.TargetRoleType
        };

        private static TableModel Clone(TableModel model, ImmutableArray<ReferenceModel> references) => new()
        {
            Namespace = model.Namespace, Name = model.Name, Tag = model.Tag,
            MinimumHeader = model.MinimumHeader, ContractVersion = model.ContractVersion,
            GenerateStructureScan = model.GenerateStructureScan,
            VersionField = model.VersionField, SupportedVersions = model.SupportedVersions,
            RoleType = model.RoleType, RoleMembers = model.RoleMembers, SupportedRoles = model.SupportedRoles,
            Scalars = model.Scalars, Derived = model.Derived, Computed = model.Computed,
            Arrays = model.Arrays, Payloads = model.Payloads, References = references,
            Identity = model.Identity
        };
    }

    private sealed class ResolvedEquality : IEqualityComparer<ResolvedSchemas>
    {
        internal static readonly ResolvedEquality Instance = new();
        public bool Equals(ResolvedSchemas? x, ResolvedSchemas? y) => x?.Canonical == y?.Canonical;
        public int GetHashCode(ResolvedSchemas value) => value.Canonical.GetHashCode();
    }

    private sealed class TableEquality : IEqualityComparer<TableModel>
    {
        internal static readonly TableEquality Instance = new();
        public bool Equals(TableModel? x, TableModel? y) => x?.Identity == y?.Identity;
        public int GetHashCode(TableModel value) => value.Identity.GetHashCode();
    }
}
