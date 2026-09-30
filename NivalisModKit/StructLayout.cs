using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Il2CppInterop.Runtime;

namespace NivalisModKit;

/// <summary>
/// Field offsets and sizes of IL2CPP types, read from the running game, for code that works
/// with raw pointers (see <see cref="NativeHook"/>). Results are cached.
/// </summary>
public static class StructLayout
{
    static readonly Dictionary<(Type, string), int> Offsets = new();
    static readonly Dictionary<Type, int> Sizes = new();

    /// <summary>The IL2CPP class pointer for <typeparamref name="T"/>.</summary>
    /// <exception cref="TypeLoadException">The class isn't registered with IL2CPP.</exception>
    public static IntPtr ClassPointer<T>()
    {
        RuntimeHelpers.RunClassConstructor(typeof(T).TypeHandle);
        IntPtr klass = Il2CppClassPointerStore<T>.NativeClassPtr;
        if (klass == IntPtr.Zero) throw new TypeLoadException($"{typeof(T).Name} has no IL2CPP class");
        return klass;
    }

    /// <summary>True if <typeparamref name="T"/> is a value type in the game.</summary>
    public static bool IsValueType<T>() => IL2CPP.il2cpp_class_is_valuetype(ClassPointer<T>());

    /// <summary>
    /// Byte offset of <paramref name="field"/> from the pointer you hold. For value types that is
    /// the start of the unboxed struct (the IL2CPP offset minus the object header); for reference
    /// types it is the object pointer.
    /// </summary>
    /// <param name="field">The IL2CPP field name, as in research/dump.cs, e.g. "amount".</param>
    /// <exception cref="MissingFieldException">No such field.</exception>
    public static int FieldOffset<T>(string field)
    {
        if (Offsets.TryGetValue((typeof(T), field), out int cached)) return cached;

        IntPtr klass = ClassPointer<T>();
        IntPtr f = IL2CPP.GetIl2CppField(klass, field);
        if (f == IntPtr.Zero) throw new MissingFieldException(typeof(T).Name, field);

        int offset = (int)IL2CPP.il2cpp_field_get_offset(f);
        if (IL2CPP.il2cpp_class_is_valuetype(klass)) offset -= 2 * IntPtr.Size;
        return Offsets[(typeof(T), field)] = offset;
    }

    /// <summary>
    /// Size in bytes: the unboxed struct for value types, the whole object (header included)
    /// for reference types.
    /// </summary>
    public static int Size<T>()
    {
        if (Sizes.TryGetValue(typeof(T), out int cached)) return cached;

        IntPtr klass = ClassPointer<T>();
        int size;
        if (IL2CPP.il2cpp_class_is_valuetype(klass))
        {
            uint align = 0;
            size = (int)IL2CPP.il2cpp_class_value_size(klass, ref align);
        }
        else size = (int)IL2CPP.il2cpp_class_instance_size(klass);
        return Sizes[typeof(T)] = size;
    }
}
