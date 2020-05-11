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
            sub.ExpirationDateTime = DateTime.UtcNow.AddMinutes(5);
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
            // todo: return all subscriptions
            return msg;
        }

        public async Task<ActionResult<string>> Post([FromQuery]string validationToken = null)
        {
            // handle validation
            if (!string.IsNullOrEmpty(validationToken))
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"Received Token: '{validationToken}'");
                Console.ForegroundColor = ConsoleColor.White;
                return Ok(validationToken);
            }

            // handle notifications
            using (StreamReader reader = new StreamReader(Request.Body))
            {
                string content = await reader.ReadToEndAsync();

                Console.WriteLine(content);

                var notifications = JsonConvert.DeserializeObject<Notifications>(content);

                foreach (var notification in notifications.Items)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"Received {notification.ResourceData?.ODataType} update: '{notification.Resource}', {notification.ResourceData?.Id} ");
                    Console.ForegroundColor = ConsoleColor.White;
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
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine($"Checking subscriptions {DateTime.Now.ToString("h:mm:ss.fff")}");
            Console.WriteLine($"Current subscription count {Subscriptions.Count()}");
            Console.ForegroundColor = ConsoleColor.White;

            foreach (var subscription in Subscriptions)
            {
                // if the subscription expires in the next 2 min, renew it
                if (subscription.Value.ExpirationDateTime < DateTime.UtcNow.AddMinutes(2))
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

        // private static object DeltaLink = null;

        // private static IUserDeltaCollectionPage lastPage = null;

        // private async Task CheckForUpdates()
        // {
        //     var graphClient = GetGraphClient();

        //     // get a page of users
        //     var users = await GetUsers(graphClient, DeltaLink);

        //     OutputUsers(users);

        //     // go through all of the pages so that we can get the delta link on the last page.
        //     while (users.NextPageRequest != null)
        //     {
        //         users = users.NextPageRequest.GetAsync().Result;
        //         OutputUsers(users);
        //     }

        //     object deltaLink;

        //     if (users.AdditionalData.TryGetValue("@odata.deltaLink", out deltaLink))
        //     {
        //         DeltaLink = deltaLink;
        //     }
        // }

        // private void OutputUsers(IUserDeltaCollectionPage users)
        // {
        //     foreach (var user in users)
        //     {
        //         var message = $"User: {user.Id}, {user.GivenName} {user.Surname}";
        //         Console.WriteLine(message);
        //     }
        // }

        // private async Task<IUserDeltaCollectionPage> GetUsers(GraphServiceClient graphClient, object deltaLink)
        // {
        //     IUserDeltaCollectionPage page;

        //     if (lastPage == null)
        //     {
        //         page = await graphClient
        //             .Users
        //             .Delta()
        //             .Request()
        //             .GetAsync();

        //     }
        //     else
        //     {
        //         lastPage.InitializeNextPageRequest(graphClient, deltaLink.ToString());
        //         page = await lastPage.NextPageRequest.GetAsync();
        //     }

        //     lastPage = page;
        //     return page;
        // }

    }
}