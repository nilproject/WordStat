﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.InteropServices;

namespace WordStatCore
{
    public enum ArrayMode
    {
        Flat,
        Sparse
    }

    public sealed class SparseArray<TValue> : IList<TValue>, IDictionary<int, TValue>
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct _NavyItem
        {
            public uint index;
            public uint zeroContinue;
            public uint oneContinue;

            public override string ToString()
            {
                return index + "[" + zeroContinue + ";" + oneContinue + "]";
            }
        }

        private static readonly _NavyItem[] emptyNavyData = new _NavyItem[0]; // data dummy. In cases where instance of current class was created, but not used
        private static readonly TValue[] emptyData = new TValue[0];

        private ArrayMode _mode;
        private uint _pseudoLength;
        private uint _allocatedCount;
        private _NavyItem[] _navyData;
        private TValue[] _values;
        private bool _zeroExists;

        public ArrayMode Mode
        {
            get
            {
                return _mode;
            }
        }

        public int Length
        {
            get
            {
                return (int)_pseudoLength;
            }
        }

        public SparseArray(ArrayMode arrayMode = ArrayMode.Flat)
        {
            _mode = arrayMode;
            _values = emptyData;
            _navyData = emptyNavyData;
        }

        public SparseArray(int capacity)
        {
            _mode = ArrayMode.Flat;
            _values = emptyData;
            _navyData = emptyNavyData;
            if (capacity > 0)
                ensureCapacity(capacity);
        }

        public SparseArray(TValue[] values)
        {
            _mode = ArrayMode.Flat;
            this._values = values;
            _navyData = emptyNavyData;
            _allocatedCount = (_pseudoLength = (uint)values.Length);
        }

        #region Члены IList<TValue>

        public int IndexOf(TValue item)
        {
            for (var i = 0; i < _allocatedCount; i++)
            {
                if (object.Equals(_values[i], item))
                {
                    if (_mode == ArrayMode.Flat)
                        return i;
                    return (int)_navyData[i].index;
                }
            }

            return -1;
        }

        void IList<TValue>.Insert(int index, TValue item)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// If "index" not equal "Length - 1", method will fail
        /// </summary>
        /// <param name="index">Index of item for removing</param>
        public void RemoveAt(int index)
        {
            if (_pseudoLength == 0 || index != (int)(_pseudoLength - 1))
                throw new InvalidOperationException();
            this[(int)(_pseudoLength - 1)] = default(TValue);
            _pseudoLength--;
        }

        public TValue this[int index]
        {
            get
            {
                var value = default(TValue);
                TryGetValue(index, out value);
                return value;
            }
            set
            {
                if (index == -1)
                    System.Diagnostics.Debugger.Break();

                bool @default = value == null; // структуры мы будем записывать, иначе пришлось бы вызывать тяжелые операции сравнения.
                //if (navyData.Length <= allocatedCount)
                //    ensureCapacity(navyData.Length * 2);
                uint unsignedIndex = (uint)index;
                if (_mode == ArrayMode.Flat)
                {
                    if (index < 0 || index > _pseudoLength)
                    {
                        if (@default)
                        {
                            if (unsignedIndex >= _pseudoLength)
                                _pseudoLength = unsignedIndex + 1;
                            return;
                        }

                        if (unsignedIndex < 32)
                        {
                            // Покрывает много тех случаев, когда относительно маленький массив заполняют с конца.
                            // Кто-то верит, что это должно работать быстрее.
                            // Вот именно из-за таких кусков кода так и может показаться.
                            // Не время для попыток исправить мир
                            _pseudoLength = unsignedIndex + 1;
                            ensureCapacity((int)_pseudoLength);
                            this[index] = value;
                            return;
                        }
                        else
                            RebuildToSparse();
                    }
                    else
                    {
                        if (_values.Length <= index)
                            ensureCapacity(Math.Max(index + 1, _values.Length * 2));
                        if (_pseudoLength == index)
                            _pseudoLength = unsignedIndex + 1;
                        _values[index] = value;
                        return;
                    }
                }

                if (_allocatedCount == 0)
                {
                    ensureCapacity(1);
                    _allocatedCount = 1;
                }

                if (unsignedIndex < _allocatedCount)
                {
                    if (_navyData[index].index == unsignedIndex)
                    {
                        if (index == 0)
                            _zeroExists = true;

                        if (_pseudoLength <= index)
                            _pseudoLength = unsignedIndex + 1;

                        _values[index] = value;

                        return;
                    }
                }

                int bi = 31;
                for (uint i = 0, ni = 0; ; bi--)
                {
                    if (_navyData[i].index > unsignedIndex)
                    {
                        if (@default)
                        {
                            if (_pseudoLength <= unsignedIndex)
                                _pseudoLength = unsignedIndex + 1; // длина может быть меньше
                            // уже записанных элементов если эти элементы имеют значение
                            // по-умолчанию и был вызван Trim
                            return;
                        }

                        var oi = _navyData[i].index;
                        var ov = _values[i];
                        _navyData[i].index = unsignedIndex;
                        _values[i] = value;
                        if (oi < _pseudoLength)
                            this[(int)oi] = ov;
                        return;
                    }
                    else if (_navyData[i].index < unsignedIndex)
                    {
                        var b = (unsignedIndex & (1 << bi)) == 0;
                        ni = b ? _navyData[i].zeroContinue : _navyData[i].oneContinue;
                        if (ni == 0)
                        {
                            if (_pseudoLength <= unsignedIndex)
                                _pseudoLength = unsignedIndex + 1;

                            if (@default)
                                return;

                            if (b)
                                _navyData[i].zeroContinue = ni = _allocatedCount++;
                            else
                                _navyData[i].oneContinue = ni = _allocatedCount++;

                            if (_navyData.Length <= _allocatedCount)
                                ensureCapacity(_navyData.Length * 2);

                            _navyData[ni].index = unsignedIndex;
                            _values[ni] = value;
                            return;
                        }

                        i = ni;
                    }
                    else
                    {
                        _values[i] = value;
                        if (_pseudoLength <= index)
                            _pseudoLength = unsignedIndex + 1;
                        if (_allocatedCount <= i)
                            _allocatedCount = i + 1;
                        return;
                    }
                }
            }
        }

        #endregion

        #region Члены ICollection<TValue>

        public void Add(TValue item)
        {
            if (_pseudoLength == uint.MaxValue)
                throw new InvalidOperationException();

            this[(int)(_pseudoLength)] = item;
        }

        public void Clear()
        {
            while (_allocatedCount > 0)
            {
                _navyData[(int)(--_allocatedCount)] = default(_NavyItem);
                _values[(int)_allocatedCount] = default(TValue);
            }
            _pseudoLength = 0;
        }

        public bool Contains(TValue item)
        {
            return IndexOf(item) != -1;
        }

        public void CopyTo(TValue[] array, int arrayIndex)
        {
            if (array == null)
                throw new NullReferenceException();
            if (arrayIndex < 0)
                throw new ArgumentOutOfRangeException();
            if (Math.Min(_pseudoLength, int.MaxValue) - arrayIndex > array.Length)
                throw new ArgumentOutOfRangeException();
            for (var i = Math.Min(_pseudoLength, int.MaxValue) + arrayIndex; i-- > arrayIndex;)
                array[i] = default(TValue);
            foreach (var v in DirectOrder)
                if (v.Key >= 0)
                    array[v.Key + arrayIndex] = v.Value;
        }

        int ICollection<TValue>.Count
        {
            get { return (int)_pseudoLength; }
        }

        public bool IsReadOnly
        {
            get { return false; }
        }

        public bool Remove(TValue item)
        {
            throw new NotImplementedException();
        }

        #endregion

        #region Члены IEnumerable<TValue>

        public IEnumerator<TValue> GetEnumerator()
        {
            for (var i = 0u; i < _pseudoLength; i++)
                yield return this[(int)i];
        }

        #endregion

        #region Члены IEnumerable

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        #endregion

        /// <summary>
        ///
        /// </summary>
        /// <param name="index"></param>
        /// <returns>Zero if the requested index does not Exists</returns>
        public long NearestIndexNotLess(long index)
        {
            TValue value;
            return nearestIndexNotLessWithValue(index, out value);
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="index"></param>
        /// <returns>Zero if the requested index does not Exists</returns>
        private long nearestIndexNotLessWithValue(long index, out TValue value)
        {
            if (_mode == ArrayMode.Sparse)
            {
                if (_navyData.Length == 0)
                {
                    value = default(TValue);
                    return index;
                }
            }
            else
            {
                if (_values.Length < index)
                {
                    value = default(TValue);
                    return -1;
                }

                value = _values[index];
                return index;
            }

            value = default(TValue);
            int bi = 31;
            long i = 0;
            long pm = -1;
            for (; ; bi--)
            {
                if (_navyData[i].oneContinue != 0)
                    pm = i;

                i = (index & (1 << bi)) == 0 ? _navyData[i].zeroContinue : _navyData[i].oneContinue;
                if (i == 0)
                {
                    if (pm == -1)
                    {
                        value = default(TValue);
                        return -1;
                    }

                    i = _navyData[pm].oneContinue;
                    for (; ; )
                    {
                        if (_navyData[i].zeroContinue != 0)
                        {
                            i = _navyData[i].zeroContinue;
                            continue;
                        }
                        if (_navyData[i].oneContinue != 0)
                        {
                            i = _navyData[i].oneContinue;
                            continue;
                        }
                        break;
                    }
                }

                if (_navyData[i].index >= (uint)index)
                {
                    value = _values[i];
                    return _navyData[i].index;
                }
            }
        }

        public long NearestIndexNotMore(long index)
        {
            if (_mode == ArrayMode.Sparse)
            {
                if (_navyData.Length == 0)
                    return 0;
            }
            else
            {
                return Math.Min(_values.Length, index);
            }
            int bi = 31;
            long i = 0;
            for (; ; bi--)
            {
                var ni = (index & (1 << bi)) == 0 ? _navyData[i].zeroContinue : _navyData[i].oneContinue;
                if (ni == 0 || _navyData[ni].index > index)
                    index = _navyData[i].index;
                else
                    i = ni;
                if (_navyData[i].index == index)
                {
                    return _navyData[i].index;
                }
            }
        }

        public IEnumerable<KeyValuePair<int, TValue>> DirectOrder
        {
            get
            {
                var index = 1U;
                var skipFirst = !_zeroExists;
                if (_mode == ArrayMode.Flat)
                {
                    for (var i = 0; i < _pseudoLength; i++)
                    {
                        skipFirst = true;
                        if (i >= _values.Length) // была насильно установлена длина фиктивным элементом.
                        {
                            yield return new KeyValuePair<int, TValue>((int)(_pseudoLength - 1), default(TValue));
                            yield break;
                        }

                        yield return new KeyValuePair<int, TValue>(i, _values[i]);

                        if (_mode != ArrayMode.Flat)
                        {
                            index = (uint)(i + 1);
                            break;
                        }
                    }
                }

                if (_mode == ArrayMode.Sparse) // Режим может поменяться во время итерации в режиме Flat
                {
                    if (_allocatedCount > 0)
                    {
                        if (!skipFirst)
                        {
                            yield return new KeyValuePair<int, TValue>(0, _values[0]);
                        }
                    }
                    else
                    {
                        yield return new KeyValuePair<int, TValue>((int)(_pseudoLength - 1), default(TValue));
                        yield break;
                    }

                    while (index < _pseudoLength)
                    {
                        int bi = 31;
                        long i = 0;
                        long pm = -1;
                        for (; bi >= 0; bi--)
                        {
                            var goToZero = (index & (1U << bi)) == 0;
                            if (goToZero && _navyData[i].oneContinue != 0)
                                pm = _navyData[i].oneContinue;

                            i = goToZero ? _navyData[i].zeroContinue : _navyData[i].oneContinue;

                            if (i == 0)
                            {
                                if (pm == -1)
                                {
                                    yield return new KeyValuePair<int, TValue>((int)(_pseudoLength - 1), default(TValue));
                                    yield break;
                                }

                                i = pm;
                                //for (; ; )
                                //{
                                //    if (_navyData[i].zeroContinue != 0)
                                //    {
                                //        i = _navyData[i].zeroContinue;
                                //        continue;
                                //    }
                                //    if (_navyData[i].oneContinue != 0)
                                //    {
                                //        i = _navyData[i].oneContinue;
                                //        continue;
                                //    }
                                //    break;
                                //}
                            }

                            if (_navyData[i].index >= index)
                            {
                                index = _navyData[i].index;
                                yield return new KeyValuePair<int, TValue>((int)index, _values[i]);
                                index++;
                                break;
                            }
                        }
                    }
                }
            }
        }

        public IEnumerable<KeyValuePair<int, TValue>> ReversOrder
        {
            get
            {
                var index = _pseudoLength - 1;
                if (_mode == ArrayMode.Flat)
                {
                    if (_pseudoLength > _values.Length)
                        yield return new KeyValuePair<int, TValue>((int)(_pseudoLength - 1), default(TValue));
                    for (var i = Math.Min(_values.Length, _pseudoLength); i-- > 0;)
                    {
                        if (_mode != ArrayMode.Flat)
                        {
                            index = (uint)i;
                            break;
                        }
                        yield return new KeyValuePair<int, TValue>((int)i, _values[i]);
                    }
                }
                if (_mode == ArrayMode.Sparse)
                {
                    if (_allocatedCount == 0)
                        yield break;

                    while (index > 0)
                    {
                        int bi = 31;
                        long i = 0;
                        for (; ; bi--)
                        {
                            var ni = (index & (1 << bi)) == 0 ? _navyData[i].zeroContinue : _navyData[i].oneContinue;
                            if (ni == 0 || _navyData[ni].index > index)
                                index = _navyData[i].index;
                            else
                                i = ni;
                            if (_navyData[i].index == index)
                            {
                                yield return new KeyValuePair<int, TValue>((int)index, _values[i]);
                                if (index == 0)
                                    yield break;
                                index--;
                                break;
                            }
                        }
                    }

                    yield return new KeyValuePair<int, TValue>(0, _values[0]);
                }
            }
        }

        public ICollection<int> Keys => _navyData.Take((int)_allocatedCount).Select(x => (int)x.index).ToArray();

        public ICollection<TValue> Values => new ReadOnlyCollection<TValue>(_values);

        public int Count => throw new NotImplementedException();

        /// <summary>
        /// Reduce length to "index of last item with non-default value" + 1
        /// </summary>
        public void Trim()
        {
            long len = -1;
            if (_mode == ArrayMode.Flat)
            {
                for (var i = _values.Length; i-- > 0;)
                {
                    if (!object.Equals(_values[i], default(TValue)))
                    {
                        len = i;
                        break;
                    }
                }
            }
            else
            {
                for (var i = _allocatedCount; i-- > 0;)
                {
                    if (_navyData[i].index > len && !object.Equals(_values[i], default(TValue)))
                        len = _navyData[i].index;
                }
            }
            _pseudoLength = (uint)(len + 1);
        }

        private void ensureCapacity(int p)
        {
            p = Math.Max(4, p);
            if (_values.Length >= p)
                return;

            var newValues = new TValue[p];
            if (_values != null)
                for (var i = 0; i < _values.Length; i++)
                    newValues[i] = _values[i];
            _values = newValues;
            if (_mode == ArrayMode.Sparse)
            {
                var newData = new _NavyItem[p];
                for (var i = 0; i < _navyData.Length; i++)
                    newData[i] = _navyData[i];
                _navyData = newData;
            }
        }

        public void RebuildToSparse()
        {
            _allocatedCount = 0;
            _mode = ArrayMode.Sparse;
            var len = _pseudoLength;
            if (len == 0)
            {
                ensureCapacity(0);
                return;
            }

            _navyData = new _NavyItem[_values.Length];
            var count = _values.Length;
            for (var i = 0; i < count; i++)
                this[i] = _values[i];

            if (_values.Length < len)
                this[(int)len - 1] = default(TValue);
        }

        public void Add(int key, TValue value)
        {
            throw new NotImplementedException();
        }

        public bool ContainsKey(int key)
        {
            throw new NotImplementedException();
        }

        public bool Remove(int key)
        {
            throw new NotImplementedException();
        }

        public bool TryGetValue(int key, out TValue value)
        {
            if (_mode == ArrayMode.Flat)
            {
                if (key < 0 || _pseudoLength <= key || _values.Length <= key)
                {
                    value = default(TValue);
                    return false;
                }

                value = _values[key];
                return false;
            }

            if (_navyData.Length == 0)
            {
                value = default(TValue);
                return true;
            }

            uint _index = (uint)key;
            int bi = 31;
            uint i = 0;

            if (_index < _allocatedCount)
            {
                if (_navyData[key].index == _index)
                {
                    value = _values[key];
                    return true;
                }
            }

            for (; ; bi--)
            {
                i = (_index & (1 << bi)) == 0 ? _navyData[i].zeroContinue : _navyData[i].oneContinue;
                if (i == 0)
                {
                    value = default(TValue);
                    return false;
                }
                else if (_navyData[i].index == _index)
                {
                    value = _values[i];
                    return true;
                }
            }
        }

        public void Add(KeyValuePair<int, TValue> item)
        {
            throw new NotImplementedException();
        }

        public bool Contains(KeyValuePair<int, TValue> item)
        {
            throw new NotImplementedException();
        }

        public void CopyTo(KeyValuePair<int, TValue>[] array, int arrayIndex)
        {
            throw new NotImplementedException();
        }

        public bool Remove(KeyValuePair<int, TValue> item)
        {
            throw new NotImplementedException();
        }

        IEnumerator<KeyValuePair<int, TValue>> IEnumerable<KeyValuePair<int, TValue>>.GetEnumerator()
        {
            throw new NotImplementedException();
        }
    }
}
