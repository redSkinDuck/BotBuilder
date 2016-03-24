﻿using Microsoft.Bot.Connector;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.Bot.Builder
{
#pragma warning disable CS1998

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public class LuisModel : Attribute
    {
        public readonly string luisModelUrl;

        public LuisModel(string luisModelUrl)
        {
            Field.SetNotNull(out this.luisModelUrl, nameof(luisModelUrl), luisModelUrl);
        }
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
    public class LuisIntent : Attribute
    {
        public readonly string intentName;

        public LuisIntent(string intentName)
        {
            Field.SetNotNull(out this.intentName, nameof(intentName), intentName);
        }
    }

    public class LuisResult
    {
        public Models.IntentRecommendation[] Intents { get; set; }

        public Models.EntityRecommendation[] Entities { get; set; }
    }

    public delegate Task IntentHandler(IDialogContext context, LuisResult luisResult);

    public class LuisDialog : IDialog
    {
        public readonly string subscriptionKey;
        public readonly string modelID;
        public readonly string luisUrl;

        protected readonly Dictionary<string, IntentHandler> handlerByIntent = new Dictionary<string, IntentHandler>();
        protected const string DefaultIntentHandler = "87DBD4FD7736";

        public LuisDialog()
        {
            var luisModel = ((LuisModel)this.GetType().GetCustomAttributes(typeof(LuisModel), true).FirstOrDefault())?.luisModelUrl;

            if (!string.IsNullOrEmpty(luisModel))
            {
                this.luisUrl = luisModel;
            }
            else
            {
                throw new Exception("Luis model attribute is not set for the class");
            }

            this.AddAttributeBasedHandlers();
        }

        public LuisDialog(string dialogID, string subscriptionKey, string modelID)
        {
            Field.SetNotNull(out this.subscriptionKey, nameof(subscriptionKey), subscriptionKey);
            Field.SetNotNull(out this.modelID, nameof(modelID), modelID);
            this.luisUrl = string.Format("https://api.projectoxford.ai/luis/v1/application?id={0}&subscription-key={1}&q=", this.modelID, this.subscriptionKey);
        }

        private void AddAttributeBasedHandlers()
        {
            var methods = from m in this.GetType().GetMethods()
                          let attr = m.GetCustomAttributes(typeof(LuisIntent), true)
                          where attr.Length > 0
                          select new { method = m, attributes = attr.Select(s => (LuisIntent)s).ToList() };

            var intentHandlers = from m in methods
                                 select new { method = m.method, intents = m.attributes.Select(i => i.intentName) };

            foreach (var handler in intentHandlers)
            {
                // TODO: use handler.method.CreateDelegate?
                //var intentHandler = (IntentHandler) handler.method.CreateDelegate(typeof(IntentHandler));
                var intentHandler = new IntentHandler(async (context, result) =>
                {
                    var task = (Task)handler.method.Invoke(this, new object[] { context, result });
                    await task;
                });

                foreach (var intent in handler.intents)
                {
                    var key = string.IsNullOrEmpty(intent) ? DefaultIntentHandler : intent;
                    this.handlerByIntent.Add(key, intentHandler);
                }
            }
        }

        protected virtual async Task<LuisResult> GetLuisResult(string luisUrl, string text)
        {
            var url = luisUrl + Uri.EscapeDataString(text);
            string json;
            using (HttpClient client = new HttpClient())
            {
                json = await client.GetStringAsync(url);
            }

            Debug.WriteLine(json);
            var response = JsonConvert.DeserializeObject<LuisResult>(json);
            return response;
        }

        async Task IDialog.StartAsync(IDialogContext context, IAwaitable<object> arguments)
        {
            context.Wait(MessageReceived);
        }

        protected async Task MessageReceived(IDialogContext context, IAwaitable<Message> item)
        {
            var message = await item;
            var luisRes = await GetLuisResult(this.luisUrl, message.Text);
            var intent = luisRes.Intents.FirstOrDefault(i => i.Score == luisRes.Intents.Select(t => t.Score).Max());
            IntentHandler handler;
            if (intent == null || !this.handlerByIntent.TryGetValue(intent.Intent, out handler))
            {
                handler = this.handlerByIntent[DefaultIntentHandler];
            }

            if (handler != null)
            {
                await handler(context, luisRes);
            }
            else
            {
                var text = $"LuisModel[{this.modelID}] no default intent handler.";
                throw new Exception(text);
            }
        }

        public LuisDialog On(string intent, IntentHandler intentHandler)
        {
            this.handlerByIntent.Add(intent, intentHandler);
            return this;
        }

        public LuisDialog OnDefault(IntentHandler intentHandler)
        {
            return this.On(DefaultIntentHandler, intentHandler);
        }
    }

}