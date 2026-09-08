using System.Reflection;
using System.Reflection.Emit;
using Avalonia.Headless.XUnit;
using VisualCat.App.Presentation;
using VisualCat.Domain.Entries;
using VisualCat.Domain.Filters;

namespace VisualCat.App.Tests;

/// <summary>
/// One helper composes every filter change, and nothing else writes the requested filter.
/// </summary>
/// <remarks>
/// <para>
/// Requested and applied filter state touches every existing filter mutation, so one missed
/// composition path silently loses a rapid edit: a severity toggle and a facet include a
/// moment apart both read the filter, and the second overwrites the first. The rule that
/// prevents it is that every mutation composes against the latest <em>requested</em> filter
/// and only <c>SetRequestedFilter</c> writes it.
/// </para>
/// <para>
/// A rule nothing checks is a rule until the next method is added, so this reads the compiled
/// IL rather than the source: every writer of <c>Filter</c> anywhere in the view model —
/// including inside the state machines the compiler generates for its async methods — has to
/// be one of the three sanctioned ones.
/// </para>
/// </remarks>
public sealed class FilterMutationTests
{
    /// <summary>
    /// The only places allowed to write the requested filter, and why each one is.
    /// </summary>
    /// <remarks>
    /// <c>SetRequestedFilter</c> is the composer every mutation routes through. The other two
    /// are rollbacks, not mutations: a failed query restores the requested filter to the
    /// applied one, and a rejected search pattern restores the filter that was in force before
    /// it was asked for. A fourth name here means a mutation has stopped composing.
    /// </remarks>
    private static readonly string[] SanctionedWriters =
    [
        "ApplySearchAsync",
        "RefreshAsync",
        "SetRequestedFilter",
    ];

    [Fact]
    public void OnlyTheComposingHelperAndItsTwoRollbacksWriteTheRequestedFilter()
    {
        var setter = typeof(SessionTabViewModel)
            .GetProperty(nameof(SessionTabViewModel.Filter), BindingFlags.Instance | BindingFlags.Public)!
            .GetSetMethod(nonPublic: true)!;

        var writers = MethodsCalling(typeof(SessionTabViewModel), setter)
            .Select(LogicalOwner)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(SanctionedWriters, writers);
    }

    /// <summary>
    /// Two edits a moment apart both survive, because the second composes against the first.
    /// </summary>
    [AvaloniaFact]
    public async Task ASecondEditStartedBeforeTheFirstSettlesComposesWithIt()
    {
        await using var fixture = await LiveTestWorkspaceFixture.CreateAsync(TwoDimensionLog);

        // Started, not awaited: the second edit reads the filter while the first is still
        // querying, which is exactly the interval a lost edit would disappear into.
        var first = fixture.Tab.SetLevelAsync(LogLevel.Info, included: false);
        var second = fixture.Tab.ToggleFacetAsync(FacetDimension.Tag, FacetKey.OfText("Alpha"), exclude: false);
        await Task.WhenAll(first, second);

        Assert.Equal(["Alpha"], fixture.Tab.Filter.IncludedTags.Order(StringComparer.Ordinal));
        Assert.DoesNotContain(LogLevel.Info, fixture.Tab.Filter.IncludedLevels);
        Assert.NotEmpty(fixture.Tab.Filter.IncludedLevels);

        // And what was applied is what was requested, once the queries have settled.
        PixelGestureAndTextScaleTests.PumpUntil(fixture.Window, () => !fixture.Tab.IsQueryPending);
        Assert.Equal(fixture.Tab.Filter.Fingerprint(), fixture.Tab.AppliedFilter.Fingerprint());
    }

    private const string TwoDimensionLog =
        "01-01 00:00:00.000000   100   201 I Alpha          : first record\n" +
        "01-01 00:00:01.000000   200   202 W Bravo          : second record\n";

    /// <summary>
    /// The method a reader would name, given one the compiler generated for it.
    /// </summary>
    /// <remarks>
    /// An async method's body lives in a nested <c>&lt;Name&gt;d__N</c> state machine, and a
    /// lambda's in a <c>&lt;&gt;c</c> display class. Both spell their origin between the
    /// angle brackets, which is the name the allow-list above is written in.
    /// </remarks>
    private static string LogicalOwner(MethodBase method)
    {
        foreach (var candidate in new[] { method.DeclaringType?.Name, method.Name })
        {
            if (candidate is null)
            {
                continue;
            }

            var open = candidate.IndexOf('<', StringComparison.Ordinal);
            var close = candidate.IndexOf('>', StringComparison.Ordinal);
            if (open >= 0 && close > open + 1)
            {
                return candidate[(open + 1)..close];
            }
        }

        return method.Name;
    }

    /// <summary>Every method of a type, or of a type nested inside it, that calls <paramref name="target"/>.</summary>
    private static IEnumerable<MethodBase> MethodsCalling(Type type, MethodBase target)
    {
        const BindingFlags All = BindingFlags.Instance | BindingFlags.Static |
                                 BindingFlags.Public | BindingFlags.NonPublic |
                                 BindingFlags.DeclaredOnly;
        foreach (var candidate in Types(type).SelectMany(static value =>
                     value.GetMethods(All).Cast<MethodBase>().Concat(value.GetConstructors(All))))
        {
            if (Calls(candidate, target))
            {
                yield return candidate;
            }
        }

        static IEnumerable<Type> Types(Type root)
        {
            yield return root;
            foreach (var nested in root.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
            {
                foreach (var inner in Types(nested))
                {
                    yield return inner;
                }
            }
        }
    }

    private static bool Calls(MethodBase method, MethodBase target)
    {
        byte[] il;
        try
        {
            il = method.GetMethodBody()?.GetILAsByteArray() ?? [];
        }
        catch (Exception exception) when (exception is InvalidOperationException or BadImageFormatException)
        {
            return false;
        }

        var module = method.Module;
        var typeArguments = method.DeclaringType?.IsGenericType == true
            ? method.DeclaringType.GetGenericArguments()
            : null;
        for (var offset = 0; offset < il.Length;)
        {
            var code = ReadOpCode(il, ref offset);
            if (code is null)
            {
                return false;
            }

            var operand = offset;
            if (!Advance(il, code.Value, ref offset))
            {
                return false;
            }

            if (code.Value.OperandType != OperandType.InlineMethod ||
                code.Value != OpCodes.Call && code.Value != OpCodes.Callvirt)
            {
                continue;
            }

            try
            {
                if (module.ResolveMethod(BitConverter.ToInt32(il, operand), typeArguments, null) is { } resolved &&
                    resolved.MetadataToken == target.MetadataToken &&
                    resolved.Module == target.Module)
                {
                    return true;
                }
            }
            catch (Exception exception) when (exception is ArgumentException or BadImageFormatException)
            {
                // A token this walk cannot resolve is not a call to the setter.
            }
        }

        return false;
    }

    private static readonly Dictionary<short, OpCode> KnownOpCodes = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(static field => field.FieldType == typeof(OpCode))
        .Select(static field => (OpCode)field.GetValue(null)!)
        .ToDictionary(static code => code.Value);

    private static OpCode? ReadOpCode(byte[] il, ref int offset)
    {
        var first = il[offset++];
        var value = first == 0xFE && offset < il.Length
            ? unchecked((short)(0xFE00 | il[offset++]))
            : (short)first;
        return KnownOpCodes.TryGetValue(value, out var code) ? code : null;
    }

    /// <summary>Steps over an instruction's operand, so the next byte read is an opcode.</summary>
    private static bool Advance(byte[] il, OpCode code, ref int offset)
    {
        var size = code.OperandType switch
        {
            OperandType.InlineNone => 0,
            OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
            OperandType.InlineVar => 2,
            OperandType.InlineI8 or OperandType.InlineR => 8,
            OperandType.InlineSwitch => il.Length - offset >= 4
                ? 4 + (4 * BitConverter.ToInt32(il, offset))
                : int.MaxValue,
            _ => 4,
        };

        offset += size;
        return offset <= il.Length;
    }
}
