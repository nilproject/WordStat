using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WordStatCore;

namespace WordStat
{
    class Program
    {
        private static readonly char[] charsToRemove = new[] { '\r', '\n', '\t', ' ', '.', ',', '!', '?', '-', '–', '<', '>', '(', ')', '[', ']', '{', '}', '<', '>', ':', '"', '\'' };

        static void Main(string[] args)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            var engine = new Engine(2);

            engine.AddSynonims(new[] { new[] { "идти", "итти" } });
            engine.AddNoiseWords(new[]
            {
                "с",
                "на",
                "в",
                "из",
                "под",
                "вне",
                "без",
                "ещё",
                "как",
                "почти",
                "бы",
                "не",
                "ну",
                "и",
                "или",
                "а"
            });

            var sw = Stopwatch.StartNew();
            var files = Directory.EnumerateFiles(".", "*.txt", SearchOption.AllDirectories).ToArray();
            var prevUpdate = Environment.TickCount;
            var tasks = new List<Task>();
            var sync = false;
            for (var i = 0; i < files.Length; i++)
            {
                var file = files[i];
                if (sync)
                {
                    learnOn(engine, file);
                }
                else
                {
                    tasks.Add(Task.Run(() => learnOn(engine, file)));
                    if (tasks.Count >= 200)
                    {
                        Task.WaitAll(tasks.ToArray());
                        tasks.Clear();
                    }
                }

                if (Environment.TickCount - prevUpdate >= 500)
                {
                    prevUpdate = Environment.TickCount;
                    Console.Title = i + "/" + files.Length;
                }
            }
            sw.Stop();
            Console.Title = sw.Elapsed.ToString();

            for (;;)
            {
                string request;
                IEnumerable<KeyValuePair<string, double>> result = null;

                Console.Write("Enter word: ");
                request = Console.ReadLine().Trim().ToLowerInvariant();

                //CollocationDirection direction = CollocationDirection.Both;

                if (string.IsNullOrEmpty(request))
                    continue;

                //if (request[0] == '<')
                //{
                //    direction = CollocationDirection.Left;
                //    request = request.Substring(1);
                //}
                //else if (request[0] == '>')
                //{
                //    direction = CollocationDirection.Right;
                //    request = request.Substring(1);
                //}

                var words = request.Split(' ').Select(x => x.ToLower()).ToArray();
                try
                {
                    if (words.Length == 1)
                    {
                        var word = engine.GetWordVector(request, false);
                        Console.WriteLine(word.Length);

                        //result = engine.FindSynonyms(word, 10);

                        result = engine.GetWordVector(words[0], false).FrequencyEnvironment().Take(100);
                    }
                    else if (words.Length == 3)
                    {
                        var word0 = engine.GetWordVector(words[0], false);
                        var word1 = engine.GetWordVector(words[1], false);
                        var word2 = engine.GetWordVector(words[2], false);
                        result = engine.FindSynonyms(word0 + word1 - word2, 10);
                    }
                    else
                    {
                        var temp = engine.GetWordVector(words[0], false);

                        for (var i = 1; i < words.Length; i++)
                        {
                            temp += engine.GetWordVector(words[i], false);
                        }

                        result = engine.FindSynonyms(temp, 10);
                    }
                }
                catch
                {
                    Console.WriteLine("Invalid request");
                    Console.WriteLine();
                    continue;
                }

                foreach (var syn in result)
                {
                    if (!double.IsNaN(syn.Value))
                    {
                        Console.WriteLine(syn);
                    }
                }

                Console.WriteLine();
            }
        }

        private static void learnOn(Engine engine, string filename)
        {
            using (var fileStream = new FileStream(filename, FileMode.Open, FileAccess.Read))
            using (var input = new StreamReader(fileStream, true))
            {
                var filtered = Engine.PreprocessData(input.ReadToEnd());
                engine.LearnOn(filtered);
            }
        }
    }
}
