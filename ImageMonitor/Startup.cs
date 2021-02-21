using System;
using System.Threading;
using System.Collections.Generic;
using System.Collections.Concurrent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Http;
using System.IO;
using FluentEmail.Core;
using FluentEmail.Mailgun;
using RestSharp;

namespace Monitor
{
    public class Startup
    {

        IConfiguration _config { get; }
        ConcurrentDictionary<string, string> _urls = new ConcurrentDictionary<string, string>();//C# has no ConcurrentHashSet, so using ConcurrentDictionary instead
        public Startup(IConfiguration configuration)
        {
            _config = configuration;

            Email.DefaultSender = new MailgunSender(
                               _config["Mailgun:Domain"],
                               _config["Mailgun:APIKey"]
                       );

            new Thread(() =>
            {
                while (true)
                {
                    foreach (KeyValuePair<string, string> kv in _urls)
                    {
                        var url = kv.Key;

                        var client = new RestClient(url);
                        var request = new RestRequest(Method.GET);
                        var respond = client.Execute(request);
                        if (respond.IsSuccessful)
                        {
                            Console.WriteLine("檢查成功:" + url);
                        }
                        else
                        {
                            Console.WriteLine("檢查失敗:" + url);
                            Send("網址無回應:" + url, "網址無回應:" + url);
                            _urls.TryRemove(url, out string _);
                        }
                    }

                    Thread.Sleep(TimeSpan.FromSeconds(5));
                }
            }).Start();
        }

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddRazorPages();
        }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            app.UseRouting();

            app.UseEndpoints(endpoints =>
            {

                endpoints.MapPost("/Submit", async context =>
                {
                    var qs = context.Request.QueryString;
                    var parse = System.Web.HttpUtility.ParseQueryString(qs.Value);
                    var url = parse["url"];
                    _urls.TryAdd(url, "");
                    Console.WriteLine("網址新增:" + url);
                    await context.Response.WriteAsync("ok");
                });

                endpoints.MapPost("/SendMail", async context =>
                {
                    string requestContent;

                    using (var reader = new StreamReader(context.Request.Body))
                    {
                        requestContent = await reader.ReadToEndAsync();
                    }

                    var parse = System.Web.HttpUtility.ParseQueryString(requestContent);
                    var Subject = System.Web.HttpUtility.UrlDecode(parse["Subject"]);
                    var Body = System.Web.HttpUtility.UrlDecode(parse["Body"]);

                    Send(Subject, Body);
                    await context.Response.WriteAsync("ok");
                });
            });
        }

        public FluentEmail.Core.Models.SendResponse Send(string Subject, string Body)
        {
            var myEmail = _config["MyEmail"];

            return Email
                      .From(myEmail)
                      .To(myEmail)
                      .Subject(Subject)
                      .Body(Body)
                      .Send();
        }
    }
}
