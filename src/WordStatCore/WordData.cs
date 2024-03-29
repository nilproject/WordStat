﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WordStatCore
{
    public enum CollocationDirection
    {
        Both,
        Left,
        Right
    }

    public sealed class CollocationInfo
    {
        internal int left;
        internal int right;

        public int Left
        {
            get
            {
                return left;
            }
            set
            {
                left = value;
            }
        }

        public int Right
        {
            get
            {
                return right;
            }
            set
            {
                right = value;
            }
        }

        public int Sum { get { return left + right; } }
    }

    public sealed class WordData
    {
        private SparseArray<double> data = new SparseArray<double>();
        private List<KeyValuePair<WordData, double>> skipList;
        private double _length = double.NaN;
        //private double _leftLength = double.NaN;
        //private double _rightLength = double.NaN;

        public double this[int index]
        {
            get
            {
                double value = 0;
                if (data.TryGetValue(index, out value))
                    return value;// ?? (data[index] = new CollocationInfo());
                else
                    return 0;
            }
            set
            {
                _length = double.NaN;
                //_leftLength = double.NaN;
                //_rightLength = double.NaN;
                data[index] = value;
            }
        }

        public double Length
        {
            get
            {
                if (!double.IsNaN(_length))
                    return _length;

                double result = 0;
                foreach (var value in data.Values)
                {
                    result += value * value;
                }

                return _length = Math.Sqrt(result);
            }
        }

        /*
        public double LeftLength
        {
            get
            {
                if (!double.IsNaN(_leftLength))
                    return _leftLength;

                double result = 0;

                var index = data.NearestIndexNotLess(1);

                while (index != 0)
                {
                    var info = this[index];
                    result += info.left * info.left;

                    index = data.NearestIndexNotLess(index + 1);
                }

                return _leftLength = Math.Sqrt(result);
            }
        }

        public double RightLength
        {
            get
            {
                if (!double.IsNaN(_rightLength))
                    return _rightLength;

                double result = 0;

                var index = data.NearestIndexNotLess(1);

                while (index != 0)
                {
                    var info = this[index];
                    result += info.right * info.right;

                    index = data.NearestIndexNotLess(index + 1);
                }

                return _rightLength = Math.Sqrt(result);
            }
        }
        */

        public int Id { get; internal set; }
        public string Word { get; internal set; }
        public Engine Engine { get; private set; }

        public WordData(Engine engine, string word, int id)
        {
            Word = word;
            Id = id;
            Engine = engine;

            _length = double.NaN;
            data = new SparseArray<double>(ArrayMode.Sparse);
            data[0] = 0;
        }

        private void buildSkipList()
        {
            if (skipList == null)
            {
                var list = new List<KeyValuePair<WordData, double>>();
                foreach (var item in data.DirectOrder)
                {
                    list.Add(new KeyValuePair<WordData, double>(Engine.GetWordVector(item.Key), item.Value));
                }

                if (list.Count < data.Keys.Count)
                {
                    var keysSet = new HashSet<int>(data.Keys);
                    foreach(var item in list)
                    {
                        keysSet.Remove(item.Key.Id);
                    }
                }

                skipList = list;
            }
        }

        public static WordData operator -(WordData left, WordData right)
        {
            if (left.Engine != right.Engine)
                throw new InvalidOperationException();

            var result = new WordData(left.Engine, null, -1);

            var leftEnum = left.data.DirectOrder.GetEnumerator();
            var rightEnum = right.data.DirectOrder.GetEnumerator();

            var leftLen = left.Length;
            var rightLen = right.Length;

            while (leftEnum.MoveNext() || rightEnum.MoveNext())
            {
                while (leftEnum.Current.Key < rightEnum.Current.Key)
                {
                    result[leftEnum.Current.Key] = leftEnum.Current.Value;
                    if (!leftEnum.MoveNext())
                        break;
                }

                while (rightEnum.Current.Key < leftEnum.Current.Key)
                {
                    result[rightEnum.Current.Key] = -rightEnum.Current.Value;
                    if (!rightEnum.MoveNext())
                        break;
                }

                if (leftEnum.Current.Key == rightEnum.Current.Key)
                {
                    result[leftEnum.Current.Key] = leftEnum.Current.Value / leftLen - rightEnum.Current.Value / rightLen;
                }
                else
                {
                    result[leftEnum.Current.Key] = leftEnum.Current.Value / leftLen;
                    result[rightEnum.Current.Key] = -rightEnum.Current.Value / rightLen;
                }
            }

            return result;
        }

        public static WordData operator +(WordData left, WordData right)
        {
            if (left.Engine != right.Engine)
                throw new InvalidOperationException();

            var result = new WordData(left.Engine, null, -1);

            var leftEnum = left.data.DirectOrder.GetEnumerator();
            var rightEnum = right.data.DirectOrder.GetEnumerator();

            var leftLen = left.Length;
            var rightLen = right.Length;

            while (leftEnum.MoveNext() || rightEnum.MoveNext())
            {
                while (leftEnum.Current.Key < rightEnum.Current.Key)
                {
                    result[leftEnum.Current.Key] = leftEnum.Current.Value;
                    if (!leftEnum.MoveNext())
                        break;
                }

                while (rightEnum.Current.Key < leftEnum.Current.Key)
                {
                    result[rightEnum.Current.Key] = rightEnum.Current.Value;
                    if (!rightEnum.MoveNext())
                        break;
                }

                if (leftEnum.Current.Key == rightEnum.Current.Key)
                {
                    result[leftEnum.Current.Key] = leftEnum.Current.Value / leftLen + rightEnum.Current.Value / rightLen;
                }
                else
                {
                    result[leftEnum.Current.Key] = leftEnum.Current.Value / leftLen;
                    result[rightEnum.Current.Key] = rightEnum.Current.Value / rightLen;
                }
            }

            return result;
        }

        public double SemanticProximity(WordData anotherWord)
        {
            if (Engine != anotherWord.Engine)
                throw new InvalidOperationException();

            double result = 0;

            this.buildSkipList();
            anotherWord.buildSkipList();

            var filter = 7 * (this.Engine.WindowSize - 1);
            if (this.Length < filter || anotherWord.Length < filter)
                return 0;

            var thisIndex = 0;
            var rightIndex = 0;

            while (thisIndex < this.skipList.Count
                || rightIndex < anotherWord.skipList.Count)
            {
                if (thisIndex == this.skipList.Count || rightIndex == anotherWord.skipList.Count)
                {
                    break;
                }

                if ((thisIndex != this.skipList.Count
                        && this.skipList[thisIndex].Key.Id < anotherWord.skipList[rightIndex].Key.Id))
                {
                    thisIndex++;
                }
                else if ((rightIndex != anotherWord.skipList.Count
                        && this.skipList[thisIndex].Key.Id > anotherWord.skipList[rightIndex].Key.Id))
                {
                    rightIndex++;
                }
                else
                {
                    double t;
                    /*
                    switch (direction)
                    {
                        case CollocationDirection.Left:
                            {
                                t = this.skipList[thisIndex].Value.left * wordData.skipList[rightIndex].Value.left;

                                result += t / Math.Pow(this.skipList[thisIndex].Key.LeftLength * 0.4, 0.6);
                                break;
                            }
                        case CollocationDirection.Right:
                            {
                                t = this.skipList[thisIndex].Value.right * wordData.skipList[rightIndex].Value.right;

                                result += t / Math.Pow(this.skipList[thisIndex].Key.RightLength * 0.4, 0.6);
                                break;
                            }
                        default:
                            {
                                t = this.skipList[thisIndex].Value.Sum * wordData.skipList[rightIndex].Value.Sum;

                                result += t / Math.Pow(this.skipList[thisIndex].Key.Length * 0.4, 0.6);
                                break;
                            }
                   }
                   */

                    t = skipList[thisIndex].Value * anotherWord.skipList[rightIndex].Value;

                    result += t;

                    thisIndex++;
                    rightIndex++;
                }
            }

            /*
            switch (direction)
            {
                case CollocationDirection.Left:
                    {
                        return result / (this.LeftLength * wordData.LeftLength);
                    }
                case CollocationDirection.Right:
                    {
                        return result / (this.RightLength * wordData.RightLength);
                    }
            }
            */

            return result / this.Length / anotherWord.Length;
        }

        public KeyValuePair<string, double>[] FrequencyEnvironment()
        {
            buildSkipList();

            return skipList
                .OrderByDescending(x => x.Value)
                .Select(x => new KeyValuePair<string, double>(x.Key.Word, x.Value / Length))
                .ToArray();
        }
    }
}
