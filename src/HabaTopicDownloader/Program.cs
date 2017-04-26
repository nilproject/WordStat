﻿using System;
using System.Linq;
using System.Net;
using System.IO;
using System.Threading;
using System.Xml;

namespace HabraTopicDownloader
{
    class Program
    {
        private const string _habraUrl = "https://habrahabr.ru/";
        private const string _indexPagePath = _habraUrl + "all/";
        private const string _postPagePath = _habraUrl + "post/";

        static void Main(string[] args)
        {
            downloadPosts().Wait();
        }

        private static async System.Threading.Tasks.Task downloadPosts()
        {
            var count = 100000;
            var postId = 0;
            var threadLimit = 10;
            var downloaded = 0;
            postId = await GetLastPostId();

            var startTime = Environment.TickCount;
            var runnedThreads = 0;

            for (var processed = 0; processed < count; processed++, postId--)
            {
                var expectedFileName = "habrahabr/" + postId + ".txt";

                Console.SetCursorPosition(0, 0);
                Console.WriteLine(processed + " / " + count + " " + expectedFileName);
                if (processed > 0)
                    Console.WriteLine("Remaining: " + (((long)count - processed) * (Environment.TickCount - startTime) / processed) / 1000);

                Console.WriteLine($"downloaded {downloaded}");

                if (File.Exists(expectedFileName))
                    continue;

                while (runnedThreads >= threadLimit)
                {
                    Thread.Sleep(10);
                }

                var postId_ = postId;
                Interlocked.Increment(ref runnedThreads);
                ThreadPool.QueueUserWorkItem((data) =>
                {
                    var repeat = false;
                    do
                    {
                        repeat = false;
                        try
                        {
                            downloadPost(postId_, expectedFileName);
                            Interlocked.Increment(ref downloaded);
                        }
                        catch (WebException)
                        { }
                        catch (IOException)
                        {
                            repeat = true;
                        }
                    }
                    while (repeat);

                    Interlocked.Decrement(ref runnedThreads);
                });
            }
        }

        private static async System.Threading.Tasks.Task<int> GetLastPostId()
        {
            int postId;
            using (var indexPage = (await WebRequest.Create(_indexPagePath).GetResponseAsync()).GetResponseStream())
            {
                var doc = HtmlReader.Create(indexPage);

                var div = doc.Document.SelectNodes("/html/body//h2[@class=\"post__title\"]/a[@class=\"post__title_link\"]");

                postId = int.Parse(div[0].Attributes["href"].Value.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries).Last());
            }

            return postId;
        }

        private static void downloadPost(int postId, string expectedFileName)
        {
            var requestTask = WebRequest.Create(_postPagePath + postId).GetResponseAsync();

            try
            {
                requestTask.Wait();
            }
            catch (AggregateException e)
            {
                throw e.InnerException;
            }

            using (var postPage = requestTask.Result.GetResponseStream())
            {
                var doc = HtmlReader.Create(postPage).Document;

                foreach (XmlElement br in doc.SelectNodes("//br"))
                {
                    br.AppendChild(doc.CreateTextNode(Environment.NewLine));
                }

                var content = doc.SelectNodes("/html/body//div[@class=\"content html_format\"]");

                if (content.Count > 0)
                {
                    Directory.CreateDirectory("habrahabr");

                    var title = WebUtility.HtmlDecode(doc.SelectNodes("/html/body//h1[@class=\"post__title\"]")[0].InnerText).Trim();
                    var text = WebUtility.HtmlDecode(content[0].InnerText).Trim();
                    var comments = doc.SelectNodes("/html/body//div[contains(@class, \"message\")]")
                        .Cast<XmlElement>()
                        .Select(x => WebUtility.HtmlDecode(x.InnerText).Trim());

                    using (var file = new FileStream(expectedFileName, FileMode.Create, FileAccess.ReadWrite))
                    using (var writer = new StreamWriter(file))
                    {
                        writer.WriteLine(title);
                        writer.WriteLine();

                        writer.WriteLine(text);
                        writer.WriteLine();

                        foreach (var comment in comments)
                        {
                            writer.WriteLine(comment.Trim());
                            writer.WriteLine();
                        }
                    }
                }
            }
        }
    }
}