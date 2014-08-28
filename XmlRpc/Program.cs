using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using XmlRpc;
using System.Xml.Linq;
using System.Net;
using System.IO;
using System.Text.RegularExpressions;

namespace CppblogTest
{
    static class Program
    {
        static void Main(string[] args)
        {
            Console.Write("username:");
            string username = Console.ReadLine();
            Console.Write("password:");
            string password = Console.ReadLine();

            Console.WriteLine("Get all post ids...");
            var postIds = GetBlogPostIds(username);
            Console.WriteLine("Get all posts...");
            var posts = GetPosts(username, password, postIds).ToArray();
            Console.WriteLine("Save posts...");
            SavePosts(posts);
            Console.WriteLine("Save images...");
            SaveImages(posts);
            Console.WriteLine("Save files...");
            SaveFiles(posts);
        }

        static string[] GetBlogPostIds(string username)
        {
            string url = string.Format("http://www.cppblog.com/{0}/GetBlogPostIds.aspx", username);

            WebRequest req = WebRequest.Create(url);
            req.Method = "GET";

            var resp = req.GetResponse();
            var sr = new StreamReader(resp.GetResponseStream());
            string content = sr.ReadToEnd();
            sr.Close();
            resp.Close();

            return content.Split(',');
        }

        static IEnumerable<Post> GetPosts(string username, string password, string[] postIds)
        {
            string url = string.Format("http://www.cppblog.com/{0}/services/metaweblog.aspx", username);
            ICppblog cppblog = XmlRpcClient.Create<ICppblog>(url);

            int count = 0;
            foreach (var id in postIds)
            {
                Post post = null;
                try
                {
                    post = cppblog.GetPost(id, username, password);
                    Console.WriteLine("Downloading Post {0}/{1}", ++count, postIds.Length);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Post {0}: {1}", id, ex.Message);
                }
                if (post != null)
                {
                    yield return post;
                }
            }
        }

        static void SaveResources(Post[] posts, string prefix, string pattern, string name, string indexFileName)
        {
            Regex regexImage = new Regex(string.Format(pattern, prefix), RegexOptions.IgnoreCase);
            var images = posts
                .SelectMany(post => regexImage.Matches(post.description).Cast<Match>())
                .Select(m => prefix + m.Groups["path"].Value)
                .Distinct()
                .ToArray();
            var imageFileNames = images
                .ToDictionary(
                    i => i,
                    i =>
                    {
                        int index = i.LastIndexOf('/');
                        return string.Format("{0}[{1}]{2}", name, Guid.NewGuid(), i.Substring(index + 1));
                    }
                    );
            new XDocument(
                new XElement("mappings",
                    imageFileNames.Select(
                        i =>
                            new XElement("mapping",
                                new XAttribute("url", i.Key),
                                new XAttribute("file", i.Value)
                                )
                        )
                        .ToArray()
                    )
                )
                .Save(string.Format(@".\CppblogPosts\{0}", indexFileName));

            int count = 0;
            byte[] buffer = new byte[65536];
            foreach (var i in imageFileNames)
            {
                try
                {
                    WebRequest request = WebRequest.Create(i.Key);
                    request.Method = "GET";
                    request.Proxy = null;
                    using (var response = request.GetResponse())
                    using (var input = response.GetResponseStream())
                    using (var output = new FileStream(string.Format(@".\CppblogPosts\{0}", i.Value), FileMode.Create))
                    {
                        while (true)
                        {
                            var length = input.Read(buffer, 0, buffer.Length);
                            if (length == 0) break;
                            output.Write(buffer, 0, length);
                        }
                    }
                    Console.WriteLine("Downloading {0} {1}/{2}", name, ++count, imageFileNames.Count);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("{0} {1}: {2}", name, i.Key, ex.Message);
                }
            }
        }

        static void SaveImages(Post[] posts)
        {
            string prefix = "http://www.cppblog.com/images/cppblog_com/";
            string pattern = @"src=""{0}(?<path>[^""]+)""";
            string name = "Image";
            string indexFileName = "Images.xml";
            SaveResources(posts, prefix, pattern, name, indexFileName);
        }

        static void SaveFiles(Post[] posts)
        {
            string prefix = "http://www.cppblog.com/Files/";
            string pattern = @"href=""{0}(?<path>[^""]+)""";
            string name = "File";
            string indexFileName = "Files.xml";
            SaveResources(posts, prefix, pattern, name, indexFileName);
        }

        static void SavePosts(Post[] posts)
        {
            Directory.CreateDirectory("CppblogPosts");
            new XDocument(
                new XElement("cppblog",
                    posts.Select(
                        p =>
                            new XElement("post",
                                new XAttribute("id", p.postid),
                                new XAttribute("date", p.dateCreated),
                                new XAttribute("title", p.title),
                                new XAttribute("url", p.link),
                                new XElement("categories",
                                    (p.categories ?? new string[0]).Select(c => new XElement("category", c)).ToArray()
                                    )
                                )
                        )
                        .ToArray()
                    )
                )
                .Save(@".\CppblogPosts\Posts.xml");
            foreach (var post in posts)
            {
                File.WriteAllText(string.Format(@".\CppblogPosts\Post[{0}].txt", post.postid), post.description, Encoding.Unicode);
            }
        }
    }
}
