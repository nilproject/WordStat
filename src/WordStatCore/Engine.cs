using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WordStatCore
{
    public sealed class Engine
    {
        private static readonly char[] splitChars = new[] { ' ', '.' };

        private StringMap<WordData> words;
        private SparseArray<WordData> wordsById;

        public int WindowSize { get; private set; }

        public int WordsCount { get { return words.Count; } }

        public Engine(int windowSize = 2)
        {
            if (windowSize < 2)
                throw new ArgumentException("windowSize");

            WindowSize = windowSize;

            words = new StringMap<WordData>()
            {
                { "", new WordData(this, "", 0) }
            };

            wordsById = new SparseArray<WordData>()
            {
                words[""]
            };
        }

        public WordData GetWordVector(string word, bool addIfNotExists)
        {
            WordData vector = null;

            if (!words.TryGetValue(word, out vector))
            {
                if (!addIfNotExists)
                    return new WordData(null, null, -1);

                words[word] = vector = new WordData(this, word, words.Count);
                wordsById[vector.Id] = vector;
            }

            return vector;
        }

        public WordData GetWordVector(int id)
        {
            return wordsById[id];
        }

        public void LearnOn(string text)
        {
            var bagOfWord = text.Split(splitChars);

            lock (words)
            {
                foreach (var word in bagOfWord)
                    GetWordVector(word, true);
            }

            int windowIndex = WindowSize - 1;
            WordData[] prevWords = new WordData[WindowSize];
            for (var i = 0; i < WindowSize - 1; i++)
            {
                prevWords[i] = GetWordVector(bagOfWord[i], false);

                if (bagOfWord[i] == "")
                    continue;

                for (var j = i - 1; j >= 0; j--)
                {
                    if (prevWords[i].Word != prevWords[j].Word)
                    {
                        lock (prevWords[i])
                        {
                            prevWords[i][prevWords[j].Id] += j + 1;
                        }
                        lock (prevWords[j])
                        {
                            prevWords[j][prevWords[i].Id] += j + 1;
                        }
                    }
                }
            }

            for (var i = WindowSize - 1; i < bagOfWord.Length; i++)
            {
                var currentVec = GetWordVector(bagOfWord[i], false);
                currentVec[0] = 0;
                prevWords[windowIndex] = currentVec;

                if (bagOfWord[i] != "")
                {
                    for (var j = windowIndex + WindowSize - 1; j > windowIndex; j--)
                    {
                        var nearWord = prevWords[j % WindowSize];
                        if (!string.IsNullOrEmpty(nearWord.Word) && nearWord.Word != currentVec.Word)
                        {
                            lock (currentVec)
                            {
                                currentVec[nearWord.Id] += (j - windowIndex);
                            }
                            lock (nearWord)
                            {
                                nearWord[currentVec.Id] += (j - windowIndex);
                            }
                        }
                    }
                }

                windowIndex++;
                windowIndex %= WindowSize;
            }
        }

        public void AddSynonims(IEnumerable<string[]> synonimsSets)
        {
            foreach (var set in synonimsSets)
            {
                if (set.Length == 0)
                    continue;

                var vector = GetWordVector(set[0], true);
                var id = vector.Id;

                for (var i = 1; i < set.Length; i++)
                {
                    var cv = GetWordVector(set[i], true);

                    vector += cv;
                    vector.Word = set[0];
                    vector.Id = id;
                }

                for (var i = 0; i < set.Length; i++)
                {
                    var cv = words[set[i]];
                    words[set[i]] = vector;
                    wordsById[cv.Id] = vector;
                }
            }
        }

        public void AddNoiseWords(IEnumerable<string> words)
        {
            _noiseWords.AddRange(words);
        }

        public WordData Substract(string leftWord, string rigthWord)
        {
            return GetWordVector(leftWord, false) - GetWordVector(rigthWord, false);
        }

        public KeyValuePair<string, double>[] FindSynonyms(string word, int count)
        {
            return FindSynonyms(GetWordVector(word, false), count);
        }

        public KeyValuePair<string, double>[] FindSynonyms(WordData wordData, int count)
        {
            if (wordData == null)
                return new KeyValuePair<string, double>[0];

            List<KeyValuePair<string, double>> result = new List<KeyValuePair<string, double>>();

            bool first = true;
            foreach (var synonym in words)
            {
                if (first)
                {
                    first = false;
                    continue;
                }

                if (synonym.Value == wordData)
                    continue;

                var s = synonym.Value.SemanticProximity(wordData);

                if (double.IsNaN(s))
                    continue;

                var i = 0;
                for (; i < result.Count; i++)
                {
                    if (result[i].Value <= s)
                        break;
                }

                if (result.Count < count || i < result.Count)
                {
                    result.Insert(i, new KeyValuePair<string, double>(synonym.Key, s));

                    if (result.Count > count)
                        result.RemoveAt(count);
                }
            }

            return result.ToArray();
        }

        private static List<string> _noiseWords = new List<string>();

        public static string PreprocessData(string text)
        {
            var openQuoteTag = "<quote";
            var closeQuoteTag = "</quote>";

            var result = new StringBuilder(text.Length);
            var quoteDepth = 0;

            for (var i = 0; i < text.Length; i++)
            {
                if (text[i] == openQuoteTag[openQuoteTag.Length - 1]
                    && i >= openQuoteTag.Length
                    && text.IndexOf(openQuoteTag, i - openQuoteTag.Length + 1, openQuoteTag.Length) == i - openQuoteTag.Length + 1)
                {
                    result.Length -= openQuoteTag.Length - 1;
                    quoteDepth++;
                }
                else if (quoteDepth > 0)
                {
                    if (text[i] == '>')
                    {
                        quoteDepth--;
                        result.Append('.');
                    }

                    /*
                    if (text[i] == closeQuoteTag[closeQuoteTag.Length - 1]
                        && text.IndexOf(closeQuoteTag, i - closeQuoteTag.Length + 1, closeQuoteTag.Length) == i - closeQuoteTag.Length + 1)
                    {
                        quoteDepth--;
                    }
                    */
                }
                else
                {
                    if (text[i] == '`' || text[i] == '’' || text[i] == '-' || text[i] == '–' || text[i] == '<')
                        result.Append(text[i]);
                    //else if (text[i] == ',')
                    //    result.Append(' ');
                    else if (char.IsPunctuation(text[i]))
                    {
                        result.Append(". ");
                    }
                    else if (char.IsWhiteSpace(text[i]))
                    {
                        if (result.Length == 0 || !char.IsWhiteSpace(result[result.Length - 1]))
                            result.Append(' ');
                    }
                    else if (text[i] == 'ё')
                        result.Append('е');
                    else if (char.IsLetterOrDigit(text[i]))
                        result.Append(char.ToLower(text[i]));

                    if (endWith(result, closeQuoteTag, 0))
                        result.Append('.');

                    for (var j = 0; j < _noiseWords.Count; j++)
                    {
                        removeWordIfNeeded(text, _noiseWords[j], result, i);
                    }
                }
            }

            return result.ToString();
        }

        private static bool removeWordIfNeeded(string text, string word, StringBuilder result, int i)
        {
            if (result.Length > 0
                && result.Length > word.Length + 2
                && (char.IsWhiteSpace(result[result.Length - 1]) || char.IsPunctuation(result[result.Length - 1]))
                && (char.IsWhiteSpace(result[result.Length - 1 - word.Length - 1]) || char.IsPunctuation(result[result.Length - 1 - word.Length - 1]))
                && endWith(result, word, 1))
            {
                var c = result[result.Length - 1];
                result.Length -= word.Length + 2;
                result.Append(c);
                return true;
            }

            return false;
        }

        private static bool endWith(StringBuilder stringBuilder, string word, int bias)
        {
            for (int i = stringBuilder.Length - bias, j = word.Length; j-- > 0 && i-- > 0;)
            {
                if (stringBuilder[i] != word[j])
                    return false;
            }

            return stringBuilder.Length - bias > word.Length;
        }
    }
}
