// Copyright (c) Microsoft Corporation. All rights reserved.	
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using msgraphapp.Models;
using Newtonsoft.Json;
using System.Net;
using System.Threading;
using Microsoft.Graph;
using Microsoft.Identity.Client;
using System.Net.Http.Headers;

namespace msgraphapp.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class NotificationsController : ControllerBase
    {
        private readonly MyConfig config;
        private static Dictionary<string, Subscription> Subscriptions = new Dictionary<string, Subscription>();
        private static Timer subscriptionTimer = null;

        public NotificationsController(MyConfig config)
        {
            this.config = config;
        }

        [HttpGet]
        public async Task<ActionResult<string>> Get()
        {
    
            var graphServiceClient = GetGraphClient();
            var msg = $"";
            Microsoft.Graph.Subscription sub = null;
            Microsoft.Graph.Subscription newSubscription = null;

            // // user changes 
            // sub = new Microsoft.Graph.Subscription();
            // sub.ChangeType = "updated";
            // sub.NotificationUrl = config.Ngrok + "/api/notifications";
            // sub.Resource = "/users";
            // sub.ExpirationDateTime = DateTime.UtcNow.AddMinutes(5);
            // sub.ClientState = "user_changes";

            // newSubscription = await graphServiceClient
            //   .Subscriptions
            //   .Request()
            //   .AddAsync(sub);
            // msg += $"Subscribed. Id: {newSubscription.Id}, {newSubscription.ChangeType}, {newSubscription.Resource}, Expiration: {newSubscription.ExpirationDateTime}\r\n";
            // Subscriptions[newSubscription.Id] = newSubscription;

            // // group changes 
            // sub = new Microsoft.Graph.Subscription();
            // sub.ChangeType = "updated";
            // sub.NotificationUrl = config.Ngrok + "/api/notifications";
            // sub.Resource = "/groups";
            // sub.ExpirationDateTime = DateTime.UtcNow.AddMinutes(5);
            // sub.ClientState = "group_changes";

            // newSubscription = await graphServiceClient
            //   .Subscriptions
            //   .Request()
            //   .AddAsync(sub);
            // msg += $"Subscribed. Id: {newSubscription.Id}, {newSubscription.ChangeType}, {newSubscription.Resource}, Expiration: {newSubscription.ExpirationDateTime}\r\n";
            // Subscriptions[newSubscription.Id] = newSubscription;

            // // New Call Details 
            sub = new Microsoft.Graph.Subscription();
            sub.ChangeType = "created";
            sub.NotificationUrl = config.Ngrok + "/api/notifications";
            sub.Resource = "/communications/callRecords";
            sub.ExpirationDateTime = DateTime.UtcNow.AddMinutes(1*60);      // 1 hour  max = 4230 minutes (under 3 days)
            sub.ClientState = "new_callrecord";

            newSubscription = await graphServiceClient
              .Subscriptions
              .Request()
              .AddAsync(sub);
            msg += $"Subscribed. Id: {newSubscription.Id}, {newSubscription.ChangeType}, {newSubscription.Resource}, Expiration: {newSubscription.ExpirationDateTime}\r\n";

            Subscriptions[newSubscription.Id] = newSubscription;
            //--------------------------


            if (subscriptionTimer == null)
            {
                subscriptionTimer = new Timer(CheckSubscriptions, null, 5000, 15000);
            }
            // return all subscriptions
            return msg;
        }

        public async Task<ActionResult<string>> Post([FromQuery]string validationToken = null)
        {
            // handle validation
            if (!string.IsNullOrEmpty(validationToken))
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"Received ValidationToken: '{validationToken}'");
                Console.ForegroundColor = ConsoleColor.White;
                return Ok(validationToken);
            }

            // handle notifications
            using (StreamReader reader = new StreamReader(Request.Body))
            {
                string content = await reader.ReadToEndAsync();
                Console.WriteLine(content);

                var notifications = JsonConvert.DeserializeObject<Notifications>(content);
                var filename = ".\\recievedchanges.csv";
                var addHeaders = ! System.IO.File.Exists(filename);
                // write/append changes to the simplets of storarage; a csv file 
                using (var writer = new StreamWriter(filename,true))
                {   
                    // header only if new file
                    if (addHeaders){
                        writer.WriteLine($"ChangeType,ClientState,TenantId,Resource,Id,ODataType,ODataId,ODataEtag");
                    }
                    foreach (var notification in notifications.Items)
                    {
                        Console.WriteLine($"Received {notification.ResourceData?.ODataType} {notification.ChangeType}: {notification.ResourceData?.ODataId} ");
                        writer.WriteLine($"{notification.ChangeType},{notification.ClientState},{notification.tenantId},{notification.Resource},"+ 
                                         $"{notification.ResourceData?.Id},{notification.ResourceData?.ODataType},{notification.ResourceData?.ODataId},"+
                                         $"{notification.ResourceData?.ODataEtag}");
                    }
                }
            }
            
            // use deltaquery to query for all updates
            // await CheckForUpdates();            

            return Ok();
        }

        private GraphServiceClient GetGraphClient()
        {
            var graphClient = new GraphServiceClient("https://graph.microsoft.com/beta", new DelegateAuthenticationProvider((requestMessage) =>
            {

                // get an access token for Graph
                var accessToken = GetAccessToken().Result;

                requestMessage
                    .Headers
                    .Authorization = new AuthenticationHeaderValue("bearer", accessToken);

                return Task.FromResult(0);
            }));

            return graphClient;
        }

        private async Task<string> GetAccessToken()
        {
            IConfidentialClientApplication app = ConfidentialClientApplicationBuilder.Create(config.AppId)
              .WithClientSecret(config.AppSecret)
              .WithAuthority($"https://login.microsoftonline.com/{config.TenantId}")
              .WithRedirectUri("https://daemon")
              .Build();

            string[] scopes = new string[] { "https://graph.microsoft.com/.default" };

            var result = await app.AcquireTokenForClient(scopes).ExecuteAsync();

            return result.AccessToken;
        }

        private void CheckSubscriptions(Object stateInfo)
        {
            AutoResetEvent autoEvent = (AutoResetEvent)stateInfo;
            // Console.ForegroundColor = ConsoleColor.Gray;
            // Console.WriteLine($"Checking subscriptions {DateTime.Now.ToString("h:mm:ss.fff")}");
            // Console.WriteLine($"Current subscription count {Subscriptions.Count()}");
            // Console.ForegroundColor = ConsoleColor.White;
            Console.Write("~");

            foreach (var subscription in Subscriptions)
            {
                // if the subscription expires in the next 5 min, renew it
                if (subscription.Value.ExpirationDateTime < DateTime.UtcNow.AddMinutes(5))
                {
                    RenewSubscription(subscription.Value);
                }
            }
        }

        private async void RenewSubscription(Subscription subscription)
        {
            Console.WriteLine($"Current subscription: {subscription.Id} for {subscription.Resource},  Expiration: {subscription.ExpirationDateTime}");

            var graphServiceClient = GetGraphClient();

            var newSubscription = new Subscription
            {
                ExpirationDateTime = DateTime.UtcNow.AddMinutes(5)
            };
            try {
                await graphServiceClient
                .Subscriptions[subscription.Id]
                .Request()
                .UpdateAsync(newSubscription);

                subscription.ExpirationDateTime = newSubscription.ExpirationDateTime;
                Console.ForegroundColor = ConsoleColor.Blue;
                Console.WriteLine($"Renewed subscription: {subscription.Id},{subscription.Resource} New Expiration: {subscription.ExpirationDateTime}");
                Console.ForegroundColor = ConsoleColor.White;
            } catch {
                // failed to renew the subscption
                // todo: create new subscription 
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Failed to renew subscription: {subscription.Id}, {subscription.Resource}");
                Console.ForegroundColor = ConsoleColor.White;
            }
        }

    }
}