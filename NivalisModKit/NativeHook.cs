using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using BepInEx.Unity.IL2CPP.Hook;

namespace NivalisModKit;

/// <summary>
/// Native detours, for methods Harmony can't patch safely (by-ref struct parameters such as
/// ShopTradeRequest or BasicTemp, where the IL2CPP trampoline passes garbage).
/// </summary>
public static class NativeHook
{
    // Native code calls the hook through a thunk owned by the delegate. If the delegate
    // is collected, the game jumps into freed memory, so the kit holds every one.
    static readonly List<Delegate> KeepAlive = new();

    /// <summary>Native code address of an interop method.</summary>
    /// <param name="type">The interop type, e.g. <c>typeof(Vendor)</c>.</param>
    /// <param name="method">
    /// The full <c>NativeMethodInfoPtr_...</c> field name from research/interop_src, or just the
    /// method name when it has one overload. Overloaded names throw, listing the full names.
    /// </param>
    /// <exception cref="MissingMethodException">No such method, or it has no native code.</exception>
    /// <exception cref="AmbiguousMatchException">A short name matched several overloads.</exception>
    public static IntPtr MethodPointer(Type type, string method)
    {
        RuntimeHelpers.RunClassConstructor(type.TypeHandle);
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;

        FieldInfo field = type.GetField(method, flags);
        if (field == null)
        {
            string prefix = $"NativeMethodInfoPtr_{method}_";
            var matches = type.GetFields(flags)
                .Where(f => f.Name.StartsWith(prefix, StringComparison.Ordinal)).ToList();
            if (matches.Count == 0)
                throw new MissingMethodException($"{type.Name}.{method} not found");
            if (matches.Count > 1)
                throw new AmbiguousMatchException($"{type.Name}.{method} has {matches.Count} overloads, " +
                    "pass the full field name: " + string.Join(", ", matches.Select(f => f.Name)));
            field = matches[0];
        }

        IntPtr info = (IntPtr)field.GetValue(null);
        if (info == IntPtr.Zero) throw new MissingMethodException($"{type.Name}.{field.Name} is null");
        IntPtr code = Marshal.ReadIntPtr(info);
        if (code == IntPtr.Zero) throw new MissingMethodException($"{type.Name}.{field.Name} has no native code");
        return code;
    }

    /// <summary>Native code address of an interop method on <typeparamref name="T"/>.</summary>
    /// <inheritdoc cref="MethodPointer(Type, string)"/>
    public static IntPtr MethodPointer<T>(string method) => MethodPointer(typeof(T), method);

    /// <summary>
    /// Detours native code at <paramref name="target"/> to <paramref name="hook"/>. Keep the returned
    /// detour; the kit keeps the delegates alive. The hook must catch its own exceptions.
    /// </summary>
    /// <typeparam name="TDelegate">
    /// An <c>[UnmanagedFunctionPointer(CallingConvention.Cdecl)]</c> delegate matching the native
    /// signature: <c>self</c> first for instance methods, the MethodInfo pointer last.
    /// </typeparam>
    /// <param name="target">From <see cref="MethodPointer(Type, string)"/>.</param>
    /// <param name="hook">Your replacement.</param>
    /// <param name="original">Calls the game's original code.</param>
    public static INativeDetour Install<TDelegate>(IntPtr target, TDelegate hook, out TDelegate original)
        where TDelegate : Delegate
    {
        var detour = INativeDetour.CreateAndApply(target, hook, out original);
        KeepAlive.Add(hook);
        KeepAlive.Add(original);
        return detour;
    }

    /// <summary>Looks up <paramref name="method"/> on <typeparamref name="T"/> and detours it.</summary>
    /// <inheritdoc cref="Install{TDelegate}(IntPtr, TDelegate, out TDelegate)"/>
    /// <param name="method">As for <see cref="MethodPointer(Type, string)"/>.</param>
    /// <param name="hook">Your replacement.</param>
    /// <param name="original">Calls the game's original code.</param>
    public static INativeDetour Install<T, TDelegate>(string method, TDelegate hook, out TDelegate original)
        where TDelegate : Delegate
        => Install(MethodPointer<T>(method), hook, out original);
}
