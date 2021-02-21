using System;
using System.Threading;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Google.Apis.Sheets.v4;
using Google.Apis.Services;
using Google.Apis.Sheets.v4.Data;
using HtmlAgilityPack;
using System.Text;
using RestSharp;

namespace CrawlPTT
{
    public class Startup
    {
        // ||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||
        ILogger<Startup> log;
        IConfiguration _config;
        SheetsService _sheetService;
        string _monitorUrl = @"http://172.17.0.1:5567/";
        // ||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            app.UseRouting();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapGet("/", async context =>
                {
                    await context.Response.WriteAsync("I'm alive!");
                });
            });
        }
        // ||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||

        public Startup(IConfiguration configuration)
        {
            // _config
            {
                _config = configuration;
            }

            // log
            {
                var fac = LoggerFactory.Create(builder =>
                {
                    builder.ClearProviders();
                    builder.AddDebug();
                    builder.AddConsole();
                });

                log = fac.CreateLogger<Startup>();
            }


            // _sheetService
            {
                _sheetService = new SheetsService(new BaseClientService.Initializer
                {
                    ApplicationName = "ImageCrawlPTT",
                    ApiKey = _config["GoogleSheet:ApiKey"],
                });
            }

            // submit to monitor
            {
                var client = new RestClient(_monitorUrl + @"Submit?url=http://172.17.0.1:5566");
                var request = new RestRequest(Method.POST);
                var respond = client.Execute(request);
                if (!respond.IsSuccessful)
                {
                    throw new Exception("submit url to monitor fail");
                }
            }

            // 新文章 全文查詢
            new Thread(() =>
            {
                var urls = new HashSet<string>();

                while (true)
                {
                    GetPtt1(ref urls, GetColumn("A", "新文章包含"), GetColumn("B", "新文章排除"), 3);
                    Thread.Sleep(TimeSpan.FromSeconds(30));
                }

            }).Start();


            // 舊文章 檔名查詢
            new Thread(() =>
            {
                var oldTargets = new List<string>();
                var first = true;

                while (true)
                {
                    var newTargets = GetColumn("C", "舊文章包含");

                    if (!newTargets.SequenceEqual(oldTargets) && !first)
                    {
                        GetPTT2(newTargets);
                    }

                    oldTargets = new List<string>(newTargets);
                    first = false;
                    Thread.Sleep(TimeSpan.FromSeconds(10));
                }

            }).Start();


            // 指定文章 推修文追蹤
            new Thread(() =>
            {
                Dictionary<string, string> oldArticles = new Dictionary<string, string>();
                var first = true;

                while (true)
                {
                    var newArticles = GetPTT3("D");

                    if (first)
                    {
                        oldArticles = newArticles.ToDictionary(entry => entry.Key, entry => entry.Value);
                        first = false;
                        continue;
                    }

                    foreach (var nkv in newArticles)
                    {
                        if (oldArticles.TryGetValue(nkv.Key, out string oldValue))
                        {
                            if (nkv.Value != oldValue)
                            {
                                if (nkv.Value.StartsWith(oldValue))
                                {
                                    SendEmail("推文新增:"+nkv.Key, nkv.Value.Replace(oldValue, ""));
                                }
                                else
                                {
                                    SendEmail("修文通知:"+nkv.Key, "原來:\n\n\n"+oldValue+"\n\n\n"+"修改:\n\n\n"+nkv.Value);
                                }
                                oldArticles[nkv.Key] = nkv.Value;
                            }
                        }
                        else
                        {
                            oldArticles.Add(nkv.Key, nkv.Value);
                        }
                    }

                    Thread.Sleep(TimeSpan.FromMinutes(1));
                }

            }).Start();
        }
        // ||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||

        public IRestResponse SendEmail(string subject, string body)
        {
            var client = new RestClient(_monitorUrl + @"SendMail");
            var request = new RestRequest(Method.POST);
            request.AddHeader("Content-Type", "application/x-www-form-urlencoded");
            request.AddParameter("Subject", subject);
            request.AddParameter("Body", body);
            return client.Execute(request);
        }
        // ||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||
        public HtmlDocument GetPTTAgeCertification(string url)
        {
            try
            {
                var response = "";
                var request = System.Net.WebRequest.Create(url);
                if (request != null)
                {
                    request.Headers.Add("cookie", "over18=1;");

                    using (System.IO.Stream s = request.GetResponse().GetResponseStream())
                    {
                        using (System.IO.StreamReader sr = new System.IO.StreamReader(s))
                        {
                            response = sr.ReadToEnd();
                        }
                    }
                }

                var doc = new HtmlDocument();
                doc.LoadHtml(response);

                return doc;

            }
            catch (Exception ex)
            {
                log.LogError(
                    string.Format("get url error, url:{0} exception:{1}", url, ex.ToString())
                    );

                return null;
            }
        }
        // ||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||
        public List<string> GetColumn(string col, string colName)
        {
            string range = ("CrawlPTT" + "!" + col + ":" + col);
            SpreadsheetsResource.ValuesResource.GetRequest request = _sheetService.Spreadsheets.Values.Get(_config["GoogleSheet:ID"], range);
            ValueRange response = request.Execute();
            IList<IList<Object>> values = response.Values;
            List<string> rtn = new List<string>();

           
            for(int i=0;i<values.Count;i++)
            {
                if(values[i].Count == 0)
                {
                    continue;
                }
                else if(values[i].Count >= 2)
                {
                    throw new Exception("have more than 1 cell");
                }


                var cell = values[i][0].ToString();

                if (i == 0)
                {
                    if (cell != colName)
                    {
                        throw new Exception("辨識用欄位錯誤: " + cell + " != " + colName);
                    }
                }
                else
                {
                    rtn.Add(cell);
                }
            }

            return rtn;
        }
        // ||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||
        public void GetPtt1(ref HashSet<string> storedUrls, List<string> includes, List<string> excludes, int pages)
        {
            if (!(includes?.Count > 0))
            {
                return;
            }

            bool first = (storedUrls.Count == 0);

            var url1 = "https://www.ptt.cc/bbs/ALLPOST/index.html";

            bool hasOld = false;

            for (int i = 0; i < pages; i++)
            {
                var doc1 = GetPTTAgeCertification(url1);
                if (doc1 == null)
                {
                    return;
                }

                HtmlNodeCollection nodes2 = doc1.DocumentNode.SelectNodes("/html/body/div[2]/div[2]/div/div[2]/a");
                if (nodes2 == null)
                {
                    log.LogError("no article nodes found");
                    return;
                }

                bool hasNew = false;

                foreach (var node2 in nodes2)
                {
                    var match2 = new Regex(@"<a href=\""(.+)\"">.+\((.+)\)<.a>", RegexOptions.Compiled).Matches(node2.OuterHtml);
                    var url2 = @"https://www.ptt.cc" + match2[0].Groups[1];

                    if (storedUrls.Contains(url2))
                    {
                        hasOld = true;
                        continue;
                    }
                    else
                    {
                        storedUrls.Add(url2);
                        hasNew = true;
                    }

                    var doc2 = GetPTTAgeCertification(url2);
                    if (doc2 == null)
                    {
                        continue;
                    }

                    var articleTime = "";
                    var articleBody = "";

                    try
                    {
                        articleTime = doc2.DocumentNode.SelectSingleNode(
                            @"/html/body/div[3]/div[1]/div[4]/span[2]").InnerText.Replace(" ", "_");

                        articleBody = doc2.DocumentNode.SelectSingleNode(
                            @"/html/body/div[3]/div[1]").InnerText;
                    }
                    catch (Exception ex)
                    {
                        log.LogError(ex.ToString());
                        continue;
                    }

                    using var reader = new StringReader(articleTime + articleBody);
                    var fileName = reader.ReadLine().Replace(" ", "　").Replace("/", "／");//linux檔名限制字元

                    var utf8bytes = Encoding.UTF8.GetBytes(fileName);
                    if (utf8bytes.Length > 220) //linux檔名限制長度
                    {
                        utf8bytes = utf8bytes.Take(220).ToArray();
                        fileName = System.Text.Encoding.UTF8.GetString(utf8bytes);
                    }

                    File.WriteAllText("../PTTPosts/" + fileName + ".txt", articleBody);

                    var articleBodyToLower = articleBody.ToLower();

                    foreach (var inc in includes)
                    {
                        if (articleBodyToLower.Contains(inc.ToLower()))
                        {
                            foreach (var exc in excludes)
                            {
                                if (articleBodyToLower.Contains(exc.ToLower()))
                                {
                                    goto ENDLOOOP;
                                }
                            }

                            SendEmail("新文章:" + inc, articleBody);
                            goto ENDLOOOP;
                        }
                    }

                ENDLOOOP:
                    continue;
                }


                if (!hasNew)
                {
                    return;
                }


                try
                {
                    HtmlNode node1 = doc1.DocumentNode.SelectSingleNode("/html/body/div[2]/div[1]/div/div[2]/a[2]");
                    var match1 = new Regex(@"href=\""(.+)\""", RegexOptions.Compiled).Matches(node1.OuterHtml);
                    url1 = @"https://www.ptt.cc" + match1[0].Groups[1];
                }
                catch (Exception ex)
                {
                    log.LogError(ex.ToString());
                    return;
                }
            }

            if(!first)
            {
                if (hasOld)
                {
                    SendEmail("警告", "文章更新有點快，考慮調整頁數");
                }
                else
                {
                    SendEmail("警告", "文章更新太快，需要調整頁數");
                }
            }

            return;
        }
        // ||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||

        public IRestResponse GetPTT2(List<string> targets)
        {
            var allFiles = Directory.GetFiles("../PTTPosts/");
            var targetFiles = new List<string>();
            var targetArticles = "";

            foreach (var f in allFiles)
            {
                bool hasAll = true;

                foreach (var t in targets)
                {
                    if (!f.Contains(t))
                    {
                        hasAll = false;
                        break;
                    }
                }

                if (hasAll)
                {
                    targetFiles.Add(f);
                    if (targetFiles.Count > 10)
                    {
                        return SendEmail("警告", "搜尋到太多舊文章，請修改關鍵字");
                    }
                }
            }


            {
                foreach (var t in targetFiles)
                {
                    targetArticles += "↓↓↓↓↓↓↓↓↓↓↓↓↓↓↓↓↓↓↓↓↓↓↓↓↓↓↓↓↓↓↓↓↓↓↓↓↓↓↓↓↓↓↓↓↓↓↓↓↓↓↓↓↓\n";
                    targetArticles += File.ReadAllText(t);
                }
            }

            return SendEmail("舊文章", targetArticles);
        }
        // ||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||
        public Dictionary<string, string> GetPTT3(string col)
        {
            Dictionary<string, string> articles = new Dictionary<string, string>();

            var urls = GetColumn(col, "推修文追蹤");

            foreach (var url in urls)
            {
                var doc = GetPTTAgeCertification(url);
                if (doc == null)
                {
                    continue;
                }

                var content = "";

                try
                {
                    content = doc.DocumentNode.SelectSingleNode(@"/html/body/div[3]/div[1]").InnerText;
                }
                catch (Exception ex)
                {
                    log.LogError(ex.ToString());
                    continue;
                }

                articles.Add(url, content);
            }

            return articles;
        }
        // ||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||
    }
}
