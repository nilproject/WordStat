using System;
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
        private SparseArray<int> data = new SparseArray<int>();
        private List<KeyValuePair<WordData, int>> skipList;
        private double _length = double.NaN;
        //private double _leftLength = double.NaN;
        //private double _rightLength = double.NaN;

        public int this[int index]
        {
            get
            {
                return data[index];// ?? (data[index] = new CollocationInfo());
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

                if (skipList != null)
                {
                    for (var i = 0; i < skipList.Count; i++)
                    {
                        var t = skipList[i].Value;
                        t *= t;
                        result += t;
                    }
                }
                else
                {
                    var index = data.NearestIndexNotLess(1);

                    while (index != 0)
                    {
                        var t = this[index];
                        t *= t;
                        result += t;

                        index = data.NearestIndexNotLess(index + 1);
                    }
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
            data[0] = 0;// new CollocationInfo();
        }

        private void buildSkipList()
        {
            if (skipList == null)
            {
                var list = new List<KeyValuePair<WordData, int>>();

                for (var i = 0; i != 0 || list.Count == 0; i = data.NearestIndexNotLess(i + 1))
                {
                    list.Add(new KeyValuePair<WordData, int>(Engine.GetWordVector(i), data[i]));
                }

                skipList = list;
            }
        }

        public static WordData operator -(WordData left, WordData right)
        {
            if (left.Engine != right.Engine)
                throw new InvalidOperationException();

            var result = new WordData(left.Engine, null, -1);
            result.data[0] = left.data[0] * (int)right.Length - right.data[0] * (int)left.Length;
            /*
            result.data[0] = new CollocationInfo
            {
                left = left.data[0].left * (int)right.LeftLength - right.data[0].left * (int)left.LeftLength,
                right = left.data[0].right * (int)right.RightLength - right.data[0].right * (int)left.RightLength
            };
            */

            var leftIndex = (int)left.data.NearestIndexNotLess(1);
            var rightIndex = (int)right.data.NearestIndexNotLess(1);

            while (leftIndex != 0 || rightIndex != 0)
            {
                if (rightIndex == 0 || (leftIndex < rightIndex && leftIndex != 0))
                {
                    /*
                    result[leftIndex] = new CollocationInfo
                    {
                        left = left[leftIndex].left * (int)right.LeftLength,
                        right = left[leftIndex].right * (int)right.RightLength
                    };
                    */

                    result[leftIndex] = left[leftIndex] * (int)right.Length;

                    leftIndex = (int)left.data.NearestIndexNotLess(leftIndex + 1);
                }
                else if (leftIndex == 0 || (leftIndex > rightIndex && rightIndex != 0))
                {
                    /*
                    result[rightIndex] = new CollocationInfo
                    {
                        left = -right[leftIndex].left * (int)left.LeftLength,
                        right = -right[leftIndex].right * (int)left.RightLength
                    };
                    */

                    result[leftIndex] = -right[rightIndex] * (int)left.Length;

                    rightIndex = (int)right.data.NearestIndexNotLess(rightIndex + 1);
                }
                else
                {
                    /*
                    result.data[leftIndex] = new CollocationInfo
                    {
                        left = left.data[leftIndex].left * (int)right.LeftLength - right.data[rightIndex].left * (int)left.LeftLength,
                        right = left.data[leftIndex].right * (int)right.RightLength - right.data[rightIndex].right * (int)left.RightLength
                    };
                    */

                    result.data[leftIndex] = left.data[leftIndex] * (int)right.Length - right.data[rightIndex] * (int)left.Length;

                    leftIndex = (int)left.data.NearestIndexNotLess(leftIndex + 1);
                    rightIndex = (int)right.data.NearestIndexNotLess(rightIndex + 1);
                }
            }

            return result;
        }

        public static WordData operator +(WordData left, WordData right)
        {
            if (left.Engine != right.Engine)
                throw new InvalidOperationException();

            var result = new WordData(left.Engine, null, -1);
            var leftIndex = 0;
            var rightIndex = 0;

            while (leftIndex != 0 || rightIndex != 0)
            {
                if (rightIndex == right.skipList.Count
                    || (leftIndex != left.skipList.Count
                        && left.skipList[leftIndex].Key.Id < right.skipList[rightIndex].Key.Id))
                {
                    result[leftIndex] = left[leftIndex] * (int)right.Length;

                    leftIndex = (int)left.data.NearestIndexNotLess(leftIndex + 1);
                }
                else if (leftIndex == left.skipList.Count
                    || (rightIndex != right.skipList.Count
                        && left.skipList[leftIndex].Key.Id > right.skipList[rightIndex].Key.Id))
                {
                    result[leftIndex] = right[rightIndex] * (int)left.Length;

                    rightIndex = (int)right.data.NearestIndexNotLess(rightIndex + 1);
                }
                else
                {
                    result.data[leftIndex] =
                        left.skipList[leftIndex].Value * (int)right.Length
                        +
                        right.skipList[rightIndex].Value * (int)left.Length;

                    leftIndex = (int)left.data.NearestIndexNotLess(leftIndex + 1);
                    rightIndex = (int)right.data.NearestIndexNotLess(rightIndex + 1);
                }
            }

            return result;
        }

        public double SemanticProximity(WordData wordData)
        {
            if (Engine != wordData.Engine)
                throw new InvalidOperationException();

            double result = 0;

            this.buildSkipList();
            wordData.buildSkipList();

            var filter = 7 * (this.Engine.WindowSize - 1);
            if (this.Length < filter || wordData.Length < filter)
                return 0;

            var thisIndex = 0;
            var rightIndex = 0;

            while (thisIndex < this.skipList.Count
                || rightIndex < wordData.skipList.Count)
            {
                if (thisIndex == this.skipList.Count || rightIndex == wordData.skipList.Count)
                {
                    break;
                }

                if ((thisIndex != this.skipList.Count
                        && this.skipList[thisIndex].Key.Id < wordData.skipList[rightIndex].Key.Id))
                {
                    thisIndex++;
                }
                else if ((rightIndex != wordData.skipList.Count
                        && this.skipList[thisIndex].Key.Id > wordData.skipList[rightIndex].Key.Id))
                {
                    rightIndex++;
                }
                else
                {
                    int t;
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

                    t = skipList[thisIndex].Value * wordData.skipList[rightIndex].Value;

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

            return result / (this.Length * wordData.Length);
        }

        public KeyValuePair<string, double>[] FrequencyEnvironment()
        {
            buildSkipList();

            return skipList
                .OrderByDescending(x => x.Value)
                .Select(x => new KeyValuePair<string, double>(x.Key.Word, x.Value / Length))
                .Take(10)
                .ToArray();
        }
    }
}
