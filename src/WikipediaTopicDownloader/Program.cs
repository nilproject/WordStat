using System;
using System.Linq;
using System.Net;
using System.IO;
using System.Threading;
using System.Xml;
using System.Xml.XPath;
using NiL.HttpUtils;
using System.Text.RegularExpressions;

namespace WikipediaTopicDownloader
{
    class Program
    {
        private const string _wikiUrl = "https://ru.m.wikipedia.org/";
        private const string _randomPagePath = _wikiUrl + "wiki/%D0%A1%D0%BB%D1%83%D0%B6%D0%B5%D0%B1%D0%BD%D0%B0%D1%8F:%D0%A1%D0%BB%D1%83%D1%87%D0%B0%D0%B9%D0%BD%D0%B0%D1%8F_%D1%81%D1%82%D1%80%D0%B0%D0%BD%D0%B8%D1%86%D0%B0#/random";
        private const string _testPagePath = _wikiUrl + "wiki/Дэйлман,_Габриэль";
        private const string _targetDirectory = "wikipedia";
        private static readonly Regex _footnotes = new Regex(@"\[\d+\]");

        static void Main(string[] args)
        {
            downloadPosts();
        }

        private static void downloadPosts()
        {
            var count = 100000;
            var postId = 0;
            var threadLimit = System.Diagnostics.Debugger.IsAttached ? 1 : 20;
            var downloaded = 0;
            var startTime = Environment.TickCount;
            var runnedThreads = 0;

            Directory.CreateDirectory(_targetDirectory);

            for (var processed = 0; processed < count; processed++)
            {
                //var expectedFileName = "habrahabr/" + postId + ".txt";

                Console.SetCursorPosition(0, 0);
                Console.WriteLine(processed + " / " + count);
                if (processed > 0)
                    Console.WriteLine("Remaining: " + (((long)count - processed) * (Environment.TickCount - startTime) / processed) / 1000);

                Console.WriteLine($"downloaded {downloaded}");

                //if (File.Exists(expectedFileName))
                //    continue;

                while (runnedThreads >= threadLimit)
                    Thread.Sleep(10);

                var postId_ = postId;
                Interlocked.Increment(ref runnedThreads);
                WaitCallback action = (data) =>
                {
                    var repeat = false;
                    do
                    {
                        repeat = false;
                        try
                        {
                            downloadRandomArticle();
                            Interlocked.Increment(ref downloaded);

                            if (System.Diagnostics.Debugger.IsAttached)
                                Environment.Exit(0);
                        }
                        catch (WebException)
                        {
                        }
                        catch (IOException)
                        {
                            repeat = true;
                        }
                    }
                    while (repeat);

                    Interlocked.Decrement(ref runnedThreads);
                };

                if (System.Diagnostics.Debugger.IsAttached)
                    action(null);
                else
                    ThreadPool.QueueUserWorkItem(action);
            }
        }

        private static void downloadRandomArticle()
        {
            var requestTask = WebRequest.Create(System.Diagnostics.Debugger.IsAttached ? _testPagePath : _randomPagePath).GetResponseAsync();

            try
            {
                requestTask.Wait();
            }
            catch (AggregateException e)
            {
                throw e.InnerException;
            }

            var response = requestTask.Result;

            using (var postPage = response.GetResponseStream())
            {
                var doc = HtmlReader.Create(postPage).Document;

                foreach (XmlElement br in doc.SelectNodes("//br"))
                    br.AppendChild(doc.CreateTextNode(Environment.NewLine));

                var content = doc.SelectNodes("/html/body//div[contains(@class, \"mw-parser-output\")]");

                if (content.Count > 0)
                {
                    var fileName = _targetDirectory + "/" + Path.GetFileName(System.Net.WebUtility.UrlDecode(response.ResponseUri.AbsolutePath)) + ".txt";
                    fileName = fileName.Replace('?', '_');
                    try
                    { 
                        var text = WebUtility.HtmlDecode(content[0].InnerText).Trim();
                        text = _footnotes.Replace(text, "");
                        File.WriteAllText(fileName, text);
                    }
                    catch(Exception e)
                    {
                        Console.Error.WriteLine(e);
                        Console.Error.Write(fileName);
                    }
                }
            }
        }
    }
}
