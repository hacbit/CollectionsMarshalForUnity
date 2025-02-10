#if NET5_0_OR_GREATER
// The CollectionsMarshal type is internal in .NET 5.0 and later, so it's not necessary to define it.
#else
#pragma warning disable CS8632, CS8500

using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace System.Runtime.InteropServices
{
    internal record ListDataHelper<T>
    {
        public T[] _items;
        public int _size;
        public int _version;
    }

    internal record DictionaryDataHelper<TKey, TValue>
    {
        public int[] _buckets;
        public Entry<TKey, TValue>[] _entries;
        public int _count;
        public int _freeList;
        public int _freeCount;
        public int _version;
        public IEqualityComparer<TKey> _comparer;
        public Dictionary<TKey, TValue>.KeyCollection _keys;
        public Dictionary<TKey, TValue>.ValueCollection _values;
        public object _syncRoot;
    }

    internal struct Entry<TKey, TValue>
    {
        public uint hashCode;
        /// <summary>
        /// 0-based index of next entry in chain: -1 means end of chain
        /// also encodes whether this entry _itself_ is part of the free list by changing sign and subtracting 3,
        /// so -2 means end of free list, -3 means index 0 but on free list, -4 means index 1 but on free list, etc.
        /// </summary>
        public int next;
        public TKey key;     // Key of entry
        public TValue value; // Value of entry
    }

#region CollectionsMarshal
    /// <summary>
    /// An unsafe class that provides a set of methods to access the underlying data representations of collections.
    /// </summary>
    public static class CollectionsMarshal
    {
        /// <summary>
        /// Get a <see cref="Span{T}"/> view over a <see cref="List{T}"/>'s data.
        /// Items should not be added or removed from the <see cref="List{T}"/> while the <see cref="Span{T}"/> is in use.
        /// </summary>
        /// <param name="list">The list to get the data view over.</param>
        /// <typeparam name="T">The type of the elements in the list.</typeparam>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Span<T> AsSpan<T>(List<T>? list)
        {
            Span<T> span = default;
            if (list is not null)
            {
                // int size = list._size;
                // T[] items = list._items;
                var listData = Unsafe.As<List<T>, ListDataHelper<T>>(ref list);
                int size = listData._size;
                T[] items = listData._items;
                Debug.Assert(items is not null, "Implementation depends on List<T> always having an array.");

                if ((uint)size > (uint)items.Length)
                {
                    // List<T> was erroneously mutated concurrently with this call, leading to a count larger than its array.
                    throw new InvalidOperationException("Concurrent operations are not supported.");
                }

                Debug.Assert(typeof(T[]) == items.GetType(), "Implementation depends on List<T> always using a T[] and not U[] where U : T.");
                span = new Span<T>(items, 0, size);
            }

            return span;
        }

        /// <summary>
        /// Gets either a ref to a <typeparamref name="TValue"/> in the <see cref="Dictionary{TKey, TValue}"/> or a ref null if it does not exist in the <paramref name="dictionary"/>.
        /// </summary>
        /// <param name="dictionary">The dictionary to get the ref to <typeparamref name="TValue"/> from.</param>
        /// <param name="key">The key used for lookup.</param>
        /// <typeparam name="TKey">The type of the keys in the dictionary.</typeparam>
        /// <typeparam name="TValue">The type of the values in the dictionary.</typeparam>
        /// <remarks>
        /// Items should not be added or removed from the <see cref="Dictionary{TKey, TValue}"/> while the ref <typeparamref name="TValue"/> is in use.
        /// The ref null can be detected using System.Runtime.CompilerServices.Unsafe.IsNullRef
        /// </remarks>
        public static ref TValue GetValueRefOrNullRef<TKey, TValue>(Dictionary<TKey, TValue> dictionary, TKey key) where TKey : notnull
            => ref dictionary.FindValue(key);

        /// <summary>
        /// Gets a ref to a <typeparamref name="TValue"/> in the <see cref="Dictionary{TKey, TValue}"/>, adding a new entry with a default value if it does not exist in the <paramref name="dictionary"/>.
        /// </summary>
        /// <param name="dictionary">The dictionary to get the ref to <typeparamref name="TValue"/> from.</param>
        /// <param name="key">The key used for lookup.</param>
        /// <param name="exists">Whether or not a new entry for the given key was added to the dictionary.</param>
        /// <typeparam name="TKey">The type of the keys in the dictionary.</typeparam>
        /// <typeparam name="TValue">The type of the values in the dictionary.</typeparam>
        /// <remarks>Items should not be added to or removed from the <see cref="Dictionary{TKey, TValue}"/> while the ref <typeparamref name="TValue"/> is in use.</remarks>
        public static ref TValue? GetValueRefOrAddDefault<TKey, TValue>(Dictionary<TKey, TValue> dictionary, TKey key, out bool exists) where TKey : notnull
            => ref CollectionsMarshalHelper<TKey, TValue>.GetValueRefOrAddDefault(dictionary, key, out exists);

        /// <summary>
        /// Sets the count of the <see cref="List{T}"/> to the specified value.
        /// </summary>
        /// <param name="list">The list to set the count of.</param>
        /// <param name="count">The value to set the list's count to.</param>
        /// <typeparam name="T">The type of the elements in the list.</typeparam>
        /// <exception cref="NullReferenceException">
        /// <paramref name="list"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="count"/> is negative.
        /// </exception>
        /// <remarks>
        /// When increasing the count, uninitialized data is being exposed.
        /// </remarks>
        public static void SetCount<T>(List<T> list, int count)
        {
            if (count < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count), "Non-negative number required.");
            }

            // list._version++;
            ref var listData = ref Unsafe.As<List<T>, ListDataHelper<T>>(ref list);
            ref int version = ref listData._version;
            version++;

            ref T[] items = ref listData._items;
            ref int size = ref listData._size;

            if (count > list.Capacity)
            {
                list.Grow(count);
            }
            else if (count < /* list._size */ size && RuntimeHelpers.IsReferenceOrContainsReferences<T>())
            {
                Array.Clear(/* list._items */ items, count, /* list._size */ size - count);
            }

            // list._size = count;
            size = count;
        }
    }
#endregion

#region ListExtensions
    internal static class ListExtensions
    {
        /// <summary>
        /// Increase the capacity of this list to at least the specified <paramref name="capacity"/>.
        /// </summary>
        /// <param name="capacity">The minimum capacity to ensure.</param>
        internal static void Grow<T>(this List<T> list, int capacity)
        {
            list.Capacity = list.GetNewCapacity(capacity);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int GetNewCapacity<T>(this List<T> list, int capacity)
        {
            const int DefaultCapacity = 4;
            var listData = Unsafe.As<List<T>, ListDataHelper<T>>(ref list);
            T[] _items = listData._items;
            Debug.Assert(_items.Length < capacity);

            int newCapacity = _items.Length == 0 ? DefaultCapacity : 2 * _items.Length;

            // Allow the list to grow to maximum possible capacity (~2G elements) before encountering overflow.
            // Note that this check works even when _items.Length overflowed thanks to the (uint) cast
            if ((uint)newCapacity > /* Array.MaxLength */ 0X7FFFFFC7) newCapacity = /* Array.MaxLength */ 0X7FFFFFC7;

            // If the computed capacity is still less than specified, set to the original argument.
            // Capacities exceeding Array.MaxLength will be surfaced as OutOfMemoryException by Array.Resize.
            if (newCapacity < capacity) newCapacity = capacity;

            return newCapacity;
        }
    }
#endregion

#region DictionaryExtensions
    internal static class DictionaryExtensions
    {
        internal static ref TValue FindValue<TKey, TValue>(this Dictionary<TKey, TValue> dict, TKey key)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            ref var entry = ref Unsafe.NullRef<Entry<TKey, TValue>>();
            ref var dictionary = ref Unsafe.As<Dictionary<TKey, TValue>, DictionaryDataHelper<TKey, TValue>>(ref dict);
            ref var _buckets = ref dictionary._buckets;
            ref var _entries = ref dictionary._entries;
            ref var _comparer = ref dictionary._comparer;

            if (_buckets is not null)
            {
                Debug.Assert(_entries is not null, "expected entries to be non-null");
                var comparer = _comparer;
                if (typeof(TKey).IsValueType && // comparer can only be null for value types; enable JIT to eliminate entire if block for ref types
                    comparer == null)
                {
                    uint hashCode = (uint)key.GetHashCode();
                    int i = GetBucket(ref dictionary, hashCode);
                    var entries = _entries;
                    uint collisionCount = 0;

                    // ValueType: Devirtualize with EqualityComparer<TKey>.Default intrinsic
                    i--; // Value in _buckets is 1-based; subtract 1 from i. We do it here so it fuses with the following conditional.
                    do
                    {
                        // Test in if to drop range check for following array access
                        if ((uint)i >= (uint)entries.Length)
                        {
                            goto ReturnNotFound;
                        }

                        entry = ref entries[i];
                        if (entry.hashCode == hashCode && EqualityComparer<TKey>.Default.Equals(entry.key, key))
                        {
                            goto ReturnFound;
                        }

                        i = entry.next;

                        collisionCount++;
                    } while (collisionCount <= (uint)entries.Length);

                    // The chain of entries forms a loop; which means a concurrent update has happened.
                    // Break out of the loop and throw, rather than looping forever.
                    goto ConcurrentOperation;
                }
                else
                {
                    Debug.Assert(comparer is not null);
                    uint hashCode = (uint)comparer.GetHashCode(key);
                    int i = GetBucket(ref dictionary, hashCode);
                    var entries = _entries;
                    uint collisionCount = 0;
                    i--; // Value in _buckets is 1-based; subtract 1 from i. We do it here so it fuses with the following conditional.
                    do
                    {
                        // Test in if to drop range check for following array access
                        if ((uint)i >= (uint)entries.Length)
                        {
                            goto ReturnNotFound;
                        }

                        entry = ref entries[i];
                        if (entry.hashCode == hashCode && comparer.Equals(entry.key, key))
                        {
                            goto ReturnFound;
                        }

                        i = entry.next;

                        collisionCount++;
                    } while (collisionCount <= (uint)entries.Length);

                    // The chain of entries forms a loop; which means a concurrent update has happened.
                    // Break out of the loop and throw, rather than looping forever.
                    goto ConcurrentOperation;
                }
            }

            goto ReturnNotFound;
            
        ConcurrentOperation:
            throw new InvalidOperationException("Concurrent operations are not supported.");
        ReturnFound:
            ref TValue value = ref entry.value;
        Return:
            return ref value;
        ReturnNotFound:
            value = ref Unsafe.NullRef<TValue>();
            goto Return;
        }

        internal static ref TValue GetValueRefOrAddDefault<TKey, TValue>(this Dictionary<TKey, TValue> dictionary, TKey key, out bool exists)
        {
            throw new NotImplementedException();
        }

        internal static ref int GetBucket<TKey, TValue>(ref DictionaryDataHelper<TKey, TValue> helper, uint hashCode)
        {
            int[] buckets = helper._buckets!;
            return ref buckets[hashCode % buckets.Length];
        }

        internal static int Initialize<TKey, TValue>(this Dictionary<TKey, TValue> dictionary, int capacity)
        {
            int size = HashHelpers.GetPrime(capacity);
            var buckets = new int[size];
            var entries = new Entry<TKey, TValue>[size];

            ref var dictionaryData = ref Unsafe.As<Dictionary<TKey, TValue>, DictionaryDataHelper<TKey, TValue>>(ref dictionary);
            dictionaryData._buckets = buckets;
            dictionaryData._entries = entries;
            dictionaryData._freeList = -1;
            return size;
        }

        internal static void Resize<TKey, TValue>(ref DictionaryDataHelper<TKey, TValue> dictionary)
        {
            Resize(ref dictionary, HashHelpers.ExpandPrime(dictionary._count), false);
        }

        internal static void Resize<TKey, TValue>(ref DictionaryDataHelper<TKey, TValue> dictionary, int newSize, bool forceNewHashCodes)
        {
            int[] numArray = new int[newSize];
            for (int index = 0; index < numArray.Length; ++index)
            numArray[index] = -1;

            var destinationArray = new Entry<TKey, TValue>[newSize];
            // Array.Copy((Array) dictionary._entries, 0, (Array) destinationArray, 0, dictionary._count);
            unsafe
            {
                Unsafe.CopyBlock(
                    ref Unsafe.As<Entry<TKey, TValue>, byte>(ref destinationArray[0]),
                    ref Unsafe.As<Entry<TKey, TValue>, byte>(ref dictionary._entries[0]),
                    (uint)(Unsafe.SizeOf<Entry<TKey, TValue>>() * dictionary._count));
            }
            if (forceNewHashCodes)
            {
                for (int index = 0; index < dictionary._count; ++index)
                {
                    if ((int)destinationArray[index].hashCode != -1)
                    destinationArray[index].hashCode = (uint)dictionary._comparer.GetHashCode(destinationArray[index].key) & int.MaxValue;
                }
                }
                for (int index1 = 0; index1 < dictionary._count; ++index1)
                {
                if (destinationArray[index1].hashCode >= 0)
                {
                    int index2 = (int)(destinationArray[index1].hashCode % newSize);
                    destinationArray[index1].next = numArray[index2];
                    numArray[index2] = index1;
                }
            }
            dictionary._buckets = numArray;
            dictionary._entries = destinationArray;
        }
    }
#endregion

#region HashHelpers
    internal static class HashHelpers
    {
        public const uint HashCollisionThreshold = 100;

        // This is the maximum prime smaller than Array.MaxLength.
        public const int MaxPrimeArrayLength = 0x7FFFFFC3;

        public const int HashPrime = 101;

        // Table of prime numbers to use as hash table sizes.
        // A typical resize algorithm would pick the smallest prime number in this array
        // that is larger than twice the previous capacity.
        // Suppose our Hashtable currently has capacity x and enough elements are added
        // such that a resize needs to occur. Resizing first computes 2x then finds the
        // first prime in the table greater than 2x, i.e. if primes are ordered
        // p_1, p_2, ..., p_i, ..., it finds p_n such that p_n-1 < 2x < p_n.
        // Doubling is important for preserving the asymptotic complexity of the
        // hashtable operations such as add.  Having a prime guarantees that double
        // hashing does not lead to infinite loops.  IE, your hash function will be
        // h1(key) + i*h2(key), 0 <= i < size.  h2 and the size must be relatively prime.
        // We prefer the low computation costs of higher prime numbers over the increased
        // memory allocation of a fixed prime number i.e. when right sizing a HashSet.
        internal static ReadOnlySpan<int> Primes => new int[]
        {
            3, 7, 11, 17, 23, 29, 37, 47, 59, 71, 89, 107, 131, 163, 197, 239, 293, 353, 431, 521, 631, 761, 919,
            1103, 1327, 1597, 1931, 2333, 2801, 3371, 4049, 4861, 5839, 7013, 8419, 10103, 12143, 14591,
            17519, 21023, 25229, 30293, 36353, 43627, 52361, 62851, 75431, 90523, 108631, 130363, 156437,
            187751, 225307, 270371, 324449, 389357, 467237, 560689, 672827, 807403, 968897, 1162687, 1395263,
            1674319, 2009191, 2411033, 2893249, 3471899, 4166287, 4999559, 5999471, 7199369
        };

        public static bool IsPrime(int candidate)
        {
            if ((candidate & 1) != 0)
            {
                int limit = (int)Math.Sqrt(candidate);
                for (int divisor = 3; divisor <= limit; divisor += 2)
                {
                    if ((candidate % divisor) == 0)
                        return false;
                }
                return true;
            }
            return candidate == 2;
        }

        public static int GetPrime(int min)
        {
            if (min < 0)
                throw new ArgumentException("Capacity overflow");

            foreach (int prime in Primes)
            {
                if (prime >= min)
                    return prime;
            }

            // Outside of our predefined table. Compute the hard way.
            for (int i = (min | 1); i < int.MaxValue; i += 2)
            {
                if (IsPrime(i) && ((i - 1) % HashPrime != 0))
                    return i;
            }
            return min;
        }

        // Returns size of hashtable to grow to.
        public static int ExpandPrime(int oldSize)
        {
            int newSize = 2 * oldSize;

            // Allow the hashtables to grow to maximum possible size (~2G elements) before encountering capacity overflow.
            // Note that this check works even when _items.Length overflowed thanks to the (uint) cast
            if ((uint)newSize > MaxPrimeArrayLength && MaxPrimeArrayLength > oldSize)
            {
                Debug.Assert(MaxPrimeArrayLength == GetPrime(MaxPrimeArrayLength), "Invalid MaxPrimeArrayLength");
                return MaxPrimeArrayLength;
            }

            return GetPrime(newSize);
        }
    }
#endregion

#region CollectionsMarshalHelper
    /// <summary>
    /// A helper class containing APIs exposed through <see cref="CollectionsMarshal"/> or <see cref="CollectionExtensions"/>.
    /// These methods are relatively niche and only used in specific scenarios, so adding them in a separate type avoids
    /// the additional overhead on each <see cref="Dictionary{TKey, TValue}"/> instantiation, especially in AOT scenarios.
    /// </summary>
    internal static class CollectionsMarshalHelper<TKey, TValue>
    {
        /// <inheritdoc cref="CollectionsMarshal.GetValueRefOrAddDefault{TKey, TValue}(Dictionary{TKey, TValue}, TKey, out bool)"/>
        public static ref TValue? GetValueRefOrAddDefault(Dictionary<TKey, TValue> dict, TKey key, out bool exists)
        {
            // NOTE: this method is mirrored by Dictionary<TKey, TValue>.TryInsert above.
            // If you make any changes here, make sure to keep that version in sync as well.

            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            ref var dictionary = ref Unsafe.As<Dictionary<TKey, TValue>, DictionaryDataHelper<TKey, TValue>>(ref dict);

            if (dictionary._buckets == null)
            {
                dict.Initialize(0);
            }
            // Debug.Assert(dictionary._buckets != null);

            ref var entries = ref dictionary._entries;
            // Debug.Assert(entries != null, "expected entries to be non-null");

            IEqualityComparer<TKey>? comparer = dictionary._comparer;
            // Debug.Assert(comparer is not null || typeof(TKey).IsValueType);
            uint hashCode = (uint)((typeof(TKey).IsValueType && comparer == null) ? key.GetHashCode() : comparer!.GetHashCode(key));

            uint collisionCount = 0;
            ref int bucket = ref DictionaryExtensions.GetBucket(ref dictionary, hashCode);
            int i = bucket - 1; // Value in _buckets is 1-based

            if (typeof(TKey).IsValueType && // comparer can only be null for value types; enable JIT to eliminate entire if block for ref types
                comparer == null)
            {
                // ValueType: Devirtualize with EqualityComparer<TKey>.Default intrinsic
                while ((uint)i < (uint)entries.Length)
                {
                    if (entries[i].hashCode == hashCode && EqualityComparer<TKey>.Default.Equals(entries[i].key, key))
                    {
                        exists = true;

                        return ref entries[i].value!;
                    }

                    i = entries[i].next;

                    collisionCount++;
                    if (collisionCount > (uint)entries.Length)
                    {
                        // The chain of entries forms a loop; which means a concurrent update has happened.
                        // Break out of the loop and throw, rather than looping forever.
                        throw new InvalidOperationException("Concurrent operations are not supported.");
                    }
                }
            }
            else
            {
                // Debug.Assert(comparer is not null);
                while ((uint)i < (uint)entries.Length)
                {
                    if (entries[i].hashCode == hashCode && comparer.Equals(entries[i].key, key))
                    {
                        exists = true;

                        return ref entries[i].value!;
                    }

                    i = entries[i].next;

                    collisionCount++;
                    if (collisionCount > (uint)entries.Length)
                    {
                        // The chain of entries forms a loop; which means a concurrent update has happened.
                        // Break out of the loop and throw, rather than looping forever.
                        throw new InvalidOperationException("Concurrent operations are not supported.");
                    }
                }
            }

            const int StartOfFreeList = -3;

            int index;
            if (dictionary._freeCount > 0)
            {
                index = dictionary._freeList;
                // Debug.Assert((StartOfFreeList - entries[dictionary._freeList].next) >= -1, "shouldn't overflow because `next` cannot underflow");
                dictionary._freeList = StartOfFreeList - entries[dictionary._freeList].next;
                dictionary._freeCount--;
            }
            else
            {
                int count = dictionary._count;
                if (count == entries.Length)
                {
                    DictionaryExtensions.Resize(ref dictionary);
                    bucket = ref DictionaryExtensions.GetBucket(ref dictionary, hashCode);
                }
                index = count;
                dictionary._count = count + 1;
                entries = dictionary._entries;
            }

            ref var entry = ref entries![index];

            entry.hashCode = hashCode;
            entry.next = bucket - 1; // Value in _buckets is 1-based
            entry.key = key;
            entry.value = default!;
            bucket = index + 1; // Value in _buckets is 1-based
            dictionary._version++;

            // Value types never rehash
            if (!typeof(TKey).IsValueType && collisionCount > HashHelpers.HashCollisionThreshold /* && comparer is NonRandomizedStringEqualityComparer */)
            {
                // If we hit the collision threshold we'll need to switch to the comparer which is using randomized string hashing
                // i.e. EqualityComparer<string>.Default.
                DictionaryExtensions.Resize(ref dictionary, entries.Length, true);

                exists = false;

                // At this point the entries array has been resized, so the current reference we have is no longer valid.
                // We're forced to do a new lookup and return an updated reference to the new entry instance. This new
                // lookup is guaranteed to always find a value though and it will never return a null reference here.
                ref TValue? value = ref dict.FindValue(key)!;

                Debug.Assert(!Unsafe.IsNullRef(ref value), "the lookup result cannot be a null ref here");

                return ref value;
            }

            exists = false;

            return ref entry.value!;
        }
    }
#endregion

#region MemoryMarshal (Custom Extension)
    /// <summary>
    /// Extends the MemoryMarshal
    /// </summary>
    public static unsafe partial class MemoryMarshal_CustomExt
    {
        /// <summary>
        /// Returns a reference to the 0th element of <paramref name="array"/>. If the array is empty, returns a reference to where the 0th element
        /// would have been stored. Such a reference may be used for pinning but must never be dereferenced.
        /// </summary>
        /// <exception cref="NullReferenceException"><paramref name="array"/> is <see langword="null"/>.</exception>
        /// <remarks>
        /// This method does not perform array variance checks. The caller must manually perform any array variance checks
        /// if the caller wishes to write to the returned reference.
        /// </remarks>
        public static ref T GetArrayDataReference<T>(T[] array) => ref Unsafe.As<byte, T>(ref GetArrayDataReference(Unsafe.As<Array>(array)));

        /// <summary>
        /// Returns a reference to the 0th element of <paramref name="array"/>. If the array is empty, returns a reference to where the 0th element
        /// would have been stored. Such a reference may be used for pinning but must never be dereferenced.
        /// </summary>
        /// <exception cref="NullReferenceException"><paramref name="array"/> is <see langword="null"/>.</exception>
        /// <remarks>
        /// The caller must manually reinterpret the returned <em>ref byte</em> as a ref to the array's underlying elemental type,
        /// perhaps utilizing an API such as <em>System.Runtime.CompilerServices.Unsafe.As</em> to assist with the reinterpretation.
        /// This technique does not perform array variance checks. The caller must manually perform any array variance checks
        /// if the caller wishes to write to the returned reference.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ref byte GetArrayDataReference(Array array)
        {
            // If needed, we can save one or two instructions per call by marking this method as intrinsic and asking the JIT
            // to special-case arrays of known type and dimension.

            // See comment on RawArrayData (in RuntimeHelpers.CoreCLR.cs) for details
            return ref Unsafe.AddByteOffset(ref Unsafe.As<RawData>(array).Data, (nuint)RuntimeHelpers_CustomExt.GetMethodTable(array)->BaseSize - (nuint)(2 * sizeof(IntPtr)));
        }
    }
#endregion

#region RawData
    // Helper class to assist with unsafe pinning of arbitrary objects.
    // It's used by VM code.
    internal sealed class RawData
    {
        public byte Data;
    }
#endregion

#region RuntimeHelpers (Custom Extension)
    /// <summary>
    /// Extends the RuntimeHelpers
    /// </summary>
    internal static partial class RuntimeHelpers_CustomExt
    {
        internal static ref byte GetRawData(this object obj) =>
            ref Unsafe.As<RawData>(obj).Data;

        // Given an object reference, returns its MethodTable*.
        //
        // WARNING: The caller has to ensure that MethodTable* does not get unloaded. The most robust way
        // to achieve this is by using GC.KeepAlive on the object that the MethodTable* was fetched from, e.g.:
        //
        // MethodTable* pMT = GetMethodTable(o);
        //
        // ... work with pMT ...
        //
        // GC.KeepAlive(o);
        //
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe MethodTable* GetMethodTable(object obj)
        {
            // The body of this function will be replaced by the EE with unsafe code
            // See getILIntrinsicImplementationForRuntimeHelpers for how this happens.

            return (MethodTable*)Unsafe.Add(ref Unsafe.As<byte, IntPtr>(ref obj.GetRawData()), -1);
        }
    }
#endregion

#region MethodTable
    [StructLayout(LayoutKind.Explicit)]
    internal unsafe struct MethodTable
    {
        /// <summary>
        /// The low WORD of the first field is the component size for array and string types.
        /// </summary>
        [FieldOffset(0)]
        public ushort ComponentSize;

        /// <summary>
        /// The flags for the current method table (only for not array or string types).
        /// </summary>
        [FieldOffset(0)]
        private uint Flags;

        /// <summary>
        /// The base size of the type (used when allocating an instance on the heap).
        /// </summary>
        [FieldOffset(4)]
        public uint BaseSize;

        // See additional native members in methodtable.h, not needed here yet.
        // 0x8: m_dwFlags2 (additional flags and token in upper 24 bits)
        // 0xC: m_wNumVirtuals

        /// <summary>
        /// The number of interfaces implemented by the current type.
        /// </summary>
        [FieldOffset(0x0E)]
        public ushort InterfaceCount;

        // For DEBUG builds, there is a conditional field here (see methodtable.h again).
        // 0x10: debug_m_szClassName (display name of the class, for the debugger)

        /// <summary>
        /// A pointer to the parent method table for the current one.
        /// </summary>
        [FieldOffset(ParentMethodTableOffset)]
        public MethodTable* ParentMethodTable;

        // Additional conditional fields (see methodtable.h).
        // m_pModule
        // m_pAuxiliaryData
        // union {
        //   m_pEEClass (pointer to the EE class)
        //   m_pCanonMT (pointer to the canonical method table)
        // }

        /// <summary>
        /// This element type handle is in a union with additional info or a pointer to the interface map.
        /// Which one is used is based on the specific method table being in used (so this field is not
        /// always guaranteed to actually be a pointer to a type handle for the element type of this type).
        /// </summary>
        [FieldOffset(ElementTypeOffset)]
        public void* ElementType;

        /// <summary>
        /// This interface map used to list out the set of interfaces. Only meaningful if InterfaceCount is non-zero.
        /// </summary>
        [FieldOffset(InterfaceMapOffset)]
        public MethodTable** InterfaceMap;

        // WFLAGS_LOW_ENUM
        private const uint enum_flag_GenericsMask = 0x00000030;
        private const uint enum_flag_GenericsMask_NonGeneric = 0x00000000; // no instantiation
        private const uint enum_flag_GenericsMask_GenericInst = 0x00000010; // regular instantiation, e.g. List<String>
        private const uint enum_flag_GenericsMask_SharedInst = 0x00000020; // shared instantiation, e.g. List<__Canon> or List<MyValueType<__Canon>>
        private const uint enum_flag_GenericsMask_TypicalInst = 0x00000030; // the type instantiated at its formal parameters, e.g. List<T>
        private const uint enum_flag_HasDefaultCtor = 0x00000200;
        private const uint enum_flag_IsByRefLike = 0x00001000;

        // WFLAGS_HIGH_ENUM
        private const uint enum_flag_ContainsPointers = 0x01000000;
        private const uint enum_flag_HasComponentSize = 0x80000000;
        private const uint enum_flag_HasTypeEquivalence = 0x02000000;
        private const uint enum_flag_Category_Mask = 0x000F0000;
        private const uint enum_flag_Category_ValueType = 0x00040000;
        private const uint enum_flag_Category_Nullable = 0x00050000;
        private const uint enum_flag_Category_PrimitiveValueType = 0x00060000; // sub-category of ValueType, Enum or primitive value type
        private const uint enum_flag_Category_TruePrimitive = 0x00070000; // sub-category of ValueType, Primitive (ELEMENT_TYPE_I, etc.)
        private const uint enum_flag_Category_ValueType_Mask = 0x000C0000;
        private const uint enum_flag_Category_Interface = 0x000C0000;
        // Types that require non-trivial interface cast have this bit set in the category
        private const uint enum_flag_NonTrivialInterfaceCast = 0x00080000 // enum_flag_Category_Array
                                                             | 0x40000000 // enum_flag_ComObject
                                                             | 0x00400000 // enum_flag_ICastable;
                                                             | 0x10000000 // enum_flag_IDynamicInterfaceCastable;
                                                             | 0x00040000; // enum_flag_Category_ValueType

        private const int DebugClassNamePtr = // adjust for debug_m_szClassName
#if DEBUG
#if UNITY_64
            8
#else
            4
#endif
#else
            0
#endif
            ;

        private const int ParentMethodTableOffset = 0x10 + DebugClassNamePtr;

#if UNITY_64
        private const int ElementTypeOffset = 0x30 + DebugClassNamePtr;
#else
        private const int ElementTypeOffset = 0x20 + DebugClassNamePtr;
#endif

#if UNITY_64
        private const int InterfaceMapOffset = 0x38 + DebugClassNamePtr;
#else
        private const int InterfaceMapOffset = 0x24 + DebugClassNamePtr;
#endif

        public bool HasComponentSize => (Flags & enum_flag_HasComponentSize) != 0;

        public bool ContainsGCPointers => (Flags & enum_flag_ContainsPointers) != 0;

        public bool NonTrivialInterfaceCast => (Flags & enum_flag_NonTrivialInterfaceCast) != 0;

        public bool HasTypeEquivalence => (Flags & enum_flag_HasTypeEquivalence) != 0;

        internal static bool AreSameType(MethodTable* mt1, MethodTable* mt2) => mt1 == mt2;

        public bool HasDefaultConstructor => (Flags & (enum_flag_HasComponentSize | enum_flag_HasDefaultCtor)) == enum_flag_HasDefaultCtor;

        public bool IsMultiDimensionalArray
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                Debug.Assert(HasComponentSize);
                // See comment on RawArrayData for details
                return BaseSize > (uint)(3 * sizeof(IntPtr));
            }
        }

        // Returns rank of multi-dimensional array rank, 0 for sz arrays
        public int MultiDimensionalArrayRank
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                Debug.Assert(HasComponentSize);
                // See comment on RawArrayData for details
                return (int)((BaseSize - (uint)(3 * sizeof(IntPtr))) / (uint)(2 * sizeof(int)));
            }
        }

        public bool IsInterface => (Flags & enum_flag_Category_Mask) == enum_flag_Category_Interface;

        public bool IsValueType => (Flags & enum_flag_Category_ValueType_Mask) == enum_flag_Category_ValueType;

        public bool IsNullable => (Flags & enum_flag_Category_Mask) == enum_flag_Category_Nullable;

        public bool IsByRefLike => (Flags & (enum_flag_HasComponentSize | enum_flag_IsByRefLike)) == enum_flag_IsByRefLike;

        // Warning! UNLIKE the similarly named Reflection api, this method also returns "true" for Enums.
        public bool IsPrimitive => (Flags & enum_flag_Category_Mask) is enum_flag_Category_PrimitiveValueType or enum_flag_Category_TruePrimitive;

        public bool HasInstantiation => (Flags & enum_flag_HasComponentSize) == 0 && (Flags & enum_flag_GenericsMask) != enum_flag_GenericsMask_NonGeneric;

        public bool IsGenericTypeDefinition => (Flags & (enum_flag_HasComponentSize | enum_flag_GenericsMask)) == enum_flag_GenericsMask_TypicalInst;

        public bool IsConstructedGenericType
        {
            get
            {
                uint genericsFlags = Flags & (enum_flag_HasComponentSize | enum_flag_GenericsMask);
                return genericsFlags == enum_flag_GenericsMask_GenericInst || genericsFlags == enum_flag_GenericsMask_SharedInst;
            }
        }

        /// <summary>
        /// Gets a <see cref="TypeHandle"/> for the element type of the current type.
        /// </summary>
        /// <remarks>This method should only be called when the current <see cref="MethodTable"/> instance represents an array or string type.</remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public TypeHandle GetArrayElementTypeHandle()
        {
            Debug.Assert(HasComponentSize);

            return new(ElementType);
        }

        [MethodImpl(MethodImplOptions.InternalCall)]
        public extern uint GetNumInstanceFieldBytes();
    }
#endregion

#region TypeHandle
    /// <summary>
    /// A type handle, which can wrap either a pointer to a <c>TypeDesc</c> or to a <see cref="MethodTable"/>.
    /// </summary>
    internal unsafe struct TypeHandle
    {
        // Subset of src\vm\typehandle.h

        /// <summary>
        /// The address of the current type handle object.
        /// </summary>
        private readonly void* m_asTAddr;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public TypeHandle(void* tAddr)
        {
            m_asTAddr = tAddr;
        }

        /// <summary>
        /// Gets whether the current instance wraps a <see langword="null"/> pointer.
        /// </summary>
        public bool IsNull => m_asTAddr is null;

        /// <summary>
        /// Gets whether or not this <see cref="TypeHandle"/> wraps a <c>TypeDesc</c> pointer.
        /// Only if this returns <see langword="false"/> it is safe to call <see cref="AsMethodTable"/>.
        /// </summary>
        public bool IsTypeDesc
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ((nint)m_asTAddr & 2) != 0;
        }

        /// <summary>
        /// Gets the <see cref="MethodTable"/> pointer wrapped by the current instance.
        /// </summary>
        /// <remarks>This is only safe to call if <see cref="IsTypeDesc"/> returned <see langword="false"/>.</remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public MethodTable* AsMethodTable()
        {
            Debug.Assert(!IsTypeDesc);

            return (MethodTable*)m_asTAddr;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static TypeHandle TypeHandleOf<T>()
        {
            // return new TypeHandle((void*)RuntimeTypeHandle.ToIntPtr(typeof(T).TypeHandle));
            // RuntimeTypeHandle.ToIntPtr(value) => value.Value
            return new TypeHandle((void*)typeof(T).TypeHandle.Value);
        }
    }
#endregion

}

#endif