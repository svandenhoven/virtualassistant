#r "Newtonsoft.Json"
#load "BasicProactiveEchoDialog.csx"

using System;
using System.Net;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using Microsoft.Bot.Builder.Azure;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Connector;

public static async Task<object> Run(HttpRequestMessage req, TraceWriter log)
{
    log.Info($"Webhook was triggered!");

    // Initialize the azure bot
    using (BotService.Initialize())
    {
        // Deserialize the incoming activity
        string jsonContent = await req.Content.ReadAsStringAsync();
        var activity = JsonConvert.DeserializeObject<Activity>(jsonContent);
        
        // authenticate incoming request and add activity.ServiceUrl to MicrosoftAppCredentials.TrustedHostNames
        // if request is authenticated
        if (!await BotService.Authenticator.TryAuthenticateAsync(req, new [] {activity}, CancellationToken.None))
        {
            return BotAuthenticator.GenerateUnauthorizedResponse(req);
        }
    
        if (activity != null)
        {
            // one of these will have an interface and process it
            switch (activity.GetActivityType())
            {
                case ActivityTypes.Message:
                    await Conversation.SendAsync(activity, () => new BasicProactiveEchoDialog());
                    break;
                case ActivityTypes.ConversationUpdate:
                    var client = new ConnectorClient(new Uri(activity.ServiceUrl));
                    IConversationUpdateActivity update = activity;
                    if (update.MembersAdded.Any())
                    {
                        var reply = activity.CreateReply();
                        var newMembers = update.MembersAdded?.Where(t => t.Id != activity.Recipient.Id);
                        foreach (var newMember in newMembers)
                        {
                            reply.Text = "Welcome";
                            if (!string.IsNullOrEmpty(newMember.Name))
                            {
                                reply.Text += $" {newMember.Name}";
                            }
                            reply.Text += "!";
                            await client.Conversations.ReplyToActivityAsync(reply);
                        }
                    }
                    break;
                case ActivityTypes.Trigger:
                    // handle proactive Message from function
                    log.Info("Trigger start");
                    ITriggerActivity trigger = activity;
                    var message = JsonConvert.DeserializeObject<Message>(((JObject) trigger.Value).GetValue("Message").ToString());
                    var messageactivity = (Activity)message.ResumptionCookie.GetMessage();
                    
                    dynamic msgObj = JsonConvert.DeserializeObject(message.Text);

                    client = new ConnectorClient(new Uri(messageactivity.ServiceUrl));
                    var triggerReply = messageactivity.CreateReply();

                    List<CardAction> cardButtons = new List<CardAction>();
                    CardAction plButton = new CardAction()
                    {
                        Type = "imBack",
                        Title = "Proceed Order",
                        Value = "ProceedOrder "
                    };
                    cardButtons.Add(plButton);
                    
                    plButton = new CardAction()
                    {
                        Type = "imBack",
                        Title = "Cancel Order",
                        Value = "CancelOrder"
                    };
                    cardButtons.Add(plButton);
                    
                    var card = new ThumbnailCard()
                    {
                        Title = msgObj.Text,
                        Subtitle = "Choose action:",
                        Images = null,
                        Buttons = cardButtons
                    };
                    card.Buttons = cardButtons;
                    
                    Microsoft.Bot.Connector.Attachment plAttachment = card.ToAttachment();
                    triggerReply.Attachments = new List<Microsoft.Bot.Connector.Attachment>() { plAttachment };
                    
                    
                    await client.Conversations.ReplyToActivityAsync(triggerReply);
                    log.Info("Trigger end");
                    break;
                case ActivityTypes.ContactRelationUpdate:
                case ActivityTypes.Typing:
                case ActivityTypes.DeleteUserData:
                case ActivityTypes.Ping:
                default:
                    log.Error($"Unknown activity type ignored: {activity.GetActivityType()}"); 
                    break;
            }
        }
        return req.CreateResponse(HttpStatusCode.Accepted);
    }    
}
