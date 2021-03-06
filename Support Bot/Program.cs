﻿#pragma warning disable CS4014
using Discord;
using Discord.Commands;
using Discord.Rest;
using Discord.WebSocket;
using Persiafighter.Applications.Support_Bot.Classes;
using Persiafighter.Libraries.AI;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Runtime.ExceptionServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Persiafighter.Applications.Support_Bot
{
    public sealed class Entry
    {
        private static void Main(string[] args) =>
            new Entry().StartAsync(args).GetAwaiter().GetResult();

        private DiscordSocketClient _client;
        private Mind _mind = new Mind("DeezNutz");
        private DateTime _start = DateTime.Now;
        private Random _rnd = new Random();
        private string[] EntryArgs;

        public async Task StartAsync(string[] args)
        {
            try
            {
                EntryArgs = args;
                Configuration.EnsureExists();
                _client = new DiscordSocketClient(new DiscordSocketConfig() { LogLevel = LogSeverity.Verbose, MessageCacheSize = 1000, DefaultRetryMode = RetryMode.AlwaysRetry });
                _client.Log += (l) => Console.Out.WriteLineAsync(l.ToString());

                await _client.LoginAsync(TokenType.Bot, Configuration.Load().Token);
                await _client.StartAsync();
                await _client.SetGameAsync("Helping People ^_^ #Bot-Support");

                _client.MessageReceived += _client_MessageReceived;
                _client.MessageUpdated += _client_MessageUpdated;
                _client.MessageDeleted += _client_MessageDeleted;
                _client.Ready += _client_Ready;

                AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

                await Task.Delay(-1);
            }
            catch (Exception ex)
            {
                Console.Write(ex);
            }
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.IsTerminating)
            {
                try
                {
                    _client.LogoutAsync();
                }
                catch { }
                Process.Start(AppDomain.CurrentDomain.FriendlyName, e.ExceptionObject.ToString());
                Environment.Exit(0);
            }
        }

        private async Task _client_Ready()
        {
            string crash = "CRASH!!!\n";
            foreach (var s in EntryArgs)
                crash += s + " ";

            if (EntryArgs.Length != 0)
                await _client.GetUser(181418012400812032).SendMessageAsync(crash);
        }
        
        private async Task _client_MessageDeleted(Cacheable<IMessage, ulong> arg1, ISocketMessageChannel arg2)
        {
            try
            {
                if (!arg1.HasValue || arg1.Value == null || arg1.Value.Author.IsBot)
                    return;
                
                if (string.Equals(arg2.Name, "bot-support", StringComparison.InvariantCultureIgnoreCase))
                    await Delete(arg1.Id);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
        }
        private async Task _client_MessageUpdated(Cacheable<IMessage, ulong> arg1, SocketMessage arg2, ISocketMessageChannel arg3)
        {
            try
            {
                if (arg2 == null || arg2.Author.IsBot)
                    return;
                
                if (string.Equals(arg3.Name, "bot-support", StringComparison.InvariantCultureIgnoreCase))
                    await Update(arg2.Content, arg2.Id);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
        }
        private async Task _client_MessageReceived(SocketMessage arg)
        {
            try
            {
                if (arg.Author.IsBot)
                    return;

                var context = new SocketCommandContext(_client, arg as SocketUserMessage);

                if (arg.Author.Id == 181418012400812032)
                {
                    var command = arg.Content.Split(' ').ToList();
                    var comm = command[0];
                    command.Remove(command[0]);
                    var arguments = CheckDoubleQuotes(command);
                    if (comm.StartsWith("$"))
                    {
                        await arg.DeleteAsync();
                        OwnerCommands(comm.TrimStart('$'), arguments);
                        return;
                    }
                }

                if (_client.GetGuild(context.Guild.Id).Roles.FirstOrDefault(k => string.Equals(k.Name, "STAFF", StringComparison.InvariantCultureIgnoreCase)).Members.ToList().Exists(k => k.Id == arg.Author.Id))
                {
                    var command = arg.Content.Split(' ').ToList();
                    var comm = command[0];
                    command.Remove(command[0]);
                    var arguments = CheckDoubleQuotes(command);
                    if (comm.StartsWith(Configuration.Load().AdminCommandPrefix))
                    {
                        await arg.DeleteAsync();
                        AdminCommands(comm.TrimStart(Configuration.Load().AdminCommandPrefix.ToCharArray()), arguments, context.Channel.Id, context.Guild.Id);
                        return;
                    }
                }

                if (arg.Content.Split(' ').ToList()[0].StartsWith("!"))
                {
                    var command = arg.Content.Split(' ').ToList();
                    var comm = command[0];
                    command.Remove(command[0]);
                    var arguments = CheckDoubleQuotes(command);
                    await arg.DeleteAsync();
                    UserCommands(comm.TrimStart('!'), arguments, context.Channel.Id, context.Guild.Id, context.Guild.Users.FirstOrDefault(k => k.Id == arg.Author.Id));
                    return;
                }

                if (string.Equals(context.Channel.Name, "bot-support", StringComparison.InvariantCultureIgnoreCase))
                    AddToDecide(arg.Content, arg.Id, context.Channel.Id, context.Guild.Id);
                else if (string.Equals(context.Channel.Name, "bot-decisions", StringComparison.InvariantCultureIgnoreCase))
                    BotDecisionCommands(context);
                else if (string.Equals(context.Channel.Name, "bot-pastebin-analyzer", StringComparison.InvariantCultureIgnoreCase))
                    if (arg.Content.ToLowerInvariant().Contains("https://pastebin.com/"))
                        WriteAndExplainErrors(arg.Content.Split().First(k => k.Contains("https://pastebin.com/")), context);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
        }

        private async Task Update(string newques, ulong qid)
        {
            try
            {
                var config = Configuration.Load();
                var q = config.UndecidedQuestions.Find(k => k.Answer.MessageID == qid);
                var achan = _client.GetGuild(q.Answer.AnswerMSG.Guild).GetTextChannel(q.Answer.AnswerMSG.Channel);
                var dchan = _client.GetGuild(q.DecisionMSG.Guild).GetTextChannel(q.DecisionMSG.Channel);
                var answer = _mind.SearchAnswer(newques);
                q.Question = newques;
                if (answer != null)
                {
                    if (answer.Similarity == 1)
                    {
                        config.UndecidedQuestions.Remove(q);
                        await (await achan.GetMessageAsync(q.Answer.AnswerMSG.MSG) as SocketUserMessage).ModifyAsync(k => k.Content = answer.Answer);
                        await (await dchan.GetMessageAsync(q.DecisionMSG.MSG)).DeleteAsync();
                        config.AnsweredQuestions.Add(q.Answer);
                    }
                    else if (answer.Similarity > (double)0.5)
                    {
                        await (await dchan.GetMessageAsync(q.DecisionMSG.MSG) as SocketUserMessage).ModifyAsync(k => k.Content = "A possible answer was found, please specify if it's good with *y <index>, *d <index>, or *a <index> \"<answer>\". Here are the details:\nAnswer: " + answer.Answer + "\nQuestion: " + answer.Phrase + "\nSimilarity: " + (answer.Similarity * 100) + "%\nIndex: " + q.Index);
                        await (await achan.GetMessageAsync(q.Answer.AnswerMSG.MSG) as SocketUserMessage).ModifyAsync(k => k.Content = answer.Answer);
                    }
                    else
                    {
                        await (await dchan.GetMessageAsync(q.DecisionMSG.MSG) as SocketUserMessage).ModifyAsync(k => k.Content = "This possible question requires an answer. To discard it use *d <index>, otherwise answer it with *a <index> \"<answer>\"\nQuestion: " + newques + "\nIndex: " + q.Index);
                        await (await achan.GetMessageAsync(q.Answer.AnswerMSG.MSG) as SocketUserMessage).ModifyAsync(k => k.Content = "I'm sorry, but I currently do not have an answer for you. Staff of this discord are currently trying to get you one, so please hold tight and check this message again in a few minutes.");
                    }
                }
                else
                {
                    await (await dchan.GetMessageAsync(q.DecisionMSG.MSG) as SocketUserMessage).ModifyAsync(k => k.Content = "This possible question requires an answer. To discard it use *d <index>, otherwise answer it with *a <index> \"<answer>\"\nQuestion: " + newques + "\nIndex: " + q.Index);
                    await (await achan.GetMessageAsync(q.Answer.AnswerMSG.MSG) as SocketUserMessage).ModifyAsync(k => k.Content = "I'm sorry, but I currently do not have an answer for you. Staff of this discord are currently trying to get you one, so please hold tight and check this message again in a few minutes.");
                }
                config.SaveJson();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
        }
        private async Task Delete(ulong questionID)
        {
            try
            {
                var config = Configuration.Load();
                var q = config.UndecidedQuestions.Find(k => k.Answer.MessageID == questionID);
                var a = config.AnsweredQuestions.Find(k => k.MessageID == questionID);
                if (q != null)
                {
                    var dchan = _client.GetGuild(q.DecisionMSG.Guild).GetTextChannel(q.DecisionMSG.Channel);
                    config.UndecidedQuestions.Remove(q);
                    await (await dchan.GetMessageAsync(q.DecisionMSG.MSG)).DeleteAsync();
                    if (a == null)
                    {
                        var achan = _client.GetGuild(q.Answer.AnswerMSG.Guild).GetTextChannel(q.Answer.AnswerMSG.Channel);
                        await (await achan.GetMessageAsync(q.Answer.AnswerMSG.MSG)).DeleteAsync();
                    }
                }
                if (a != null)
                {
                    config.AnsweredQuestions.Remove(a);
                    var achan = _client.GetGuild(a.AnswerMSG.Guild).GetTextChannel(a.AnswerMSG.Channel);
                    await (await achan.GetMessageAsync(a.AnswerMSG.MSG)).DeleteAsync();
                }
                config.SaveJson();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
        }
        private async Task AddToDecide(string question, ulong qid, ulong ChannID, ulong GuildID)
        {
            try
            {
                var config = Configuration.Load();
                var channel = _client.GetGuild(GuildID).TextChannels.FirstOrDefault(k => string.Equals(k.Name, "bot-decisions", StringComparison.InvariantCultureIgnoreCase));
                var answer = _mind.SearchAnswer(question);
                if (answer != null)
                {
                    if (answer.Similarity == 1)
                    {
                        var c = await _client.GetGuild(GuildID).GetTextChannel(ChannID).SendMessageAsync(answer.Answer);
                        config.AnsweredQuestions.Add(new Answer(new Message(c.Id, ChannID, GuildID), qid));
                    }
                    else if (answer.Similarity >= (double)0.5)
                    {
                        var i = config.UndecidedQuestions.Count == 0 ? 0 : config.UndecidedQuestions.Last().Index + 1;
                        var decision = await channel.SendMessageAsync("A possible answer was found, please specify if it's good with *y <index>, *d <index>, or *a <index> \"<answer>\". Here are the details:\nAnswer: " + answer.Answer + "\nQuestion: " + answer.Phrase + "\nSimilarity: " + (answer.Similarity * 100) + "%\nIndex: " + i);
                        var tempanswer = await _client.GetGuild(GuildID).GetTextChannel(ChannID).SendMessageAsync(answer.Answer);
                        config.UndecidedQuestions.Add(new Declare(i, question, qid, new Message(decision.Id, channel.Id, GuildID), new Message(tempanswer.Id, ChannID, GuildID)));
                    }
                    else
                    {
                        var i = config.UndecidedQuestions.Count == 0 ? 0 : config.UndecidedQuestions.Last().Index + 1;
                        var decision = await channel.SendMessageAsync("This possible question requires an answer. To discard it use *d <index>, otherwise answer it with *a <index> \"<answer>\"\nQuestion: " + question + "\nIndex: " + i);
                        var tempanswer = await _client.GetGuild(GuildID).GetTextChannel(ChannID).SendMessageAsync("I'm sorry, but I currently do not have an answer for you. Staff of this discord are currently trying to get you one, so please hold tight and check this message again in a few minutes.");
                        config.UndecidedQuestions.Add(new Declare(i, question, qid, new Message(decision.Id, channel.Id, GuildID), new Message(tempanswer.Id, ChannID, GuildID)));
                    }
                }
                else
                {
                    var i = config.UndecidedQuestions.Count == 0 ? 0 : config.UndecidedQuestions.Last().Index + 1;
                    var decision = await channel.SendMessageAsync("This possible question requires an answer. To discard it use *d <index>, otherwise answer it with *a <index> \"<answer>\"\nQuestion: " + question + "\nIndex: " + i);
                    var tempanswer = await _client.GetGuild(GuildID).GetTextChannel(ChannID).SendMessageAsync("I'm sorry, but I currently do not have an answer for you. Staff of this discord are currently trying to get you one, so please hold tight and check this message again in a few minutes.");
                    config.UndecidedQuestions.Add(new Declare(i, question, qid, new Message(decision.Id, channel.Id, GuildID), new Message(tempanswer.Id, ChannID, GuildID)));
                }
                config.SaveJson();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
        }
        private async Task Discard(string index, ulong GuildID)
        {
            try
            {
                var config = Configuration.Load();
                if (int.TryParse(index, out int i) && config.UndecidedQuestions.Exists(k => k.Index == i))
                {
                    var q = config.UndecidedQuestions.Find(k => k.Index == i);
                    config.UndecidedQuestions.Remove(q);
                    await (await _client.GetGuild(q.DecisionMSG.Guild).GetTextChannel(q.DecisionMSG.Channel).GetMessageAsync(q.DecisionMSG.MSG)).DeleteAsync();
                    await (await _client.GetGuild(q.Answer.AnswerMSG.Guild).GetTextChannel(q.Answer.AnswerMSG.Channel).GetMessageAsync(q.Answer.AnswerMSG.MSG) as SocketUserMessage).ModifyAsync(k => k.Content = "Your question has been discarded by staff. This could be due to it not being an actual question or them not wanting to answer it.");
                    config.AnsweredQuestions.Add(q.Answer);
                }
                else if (string.Equals(index, "all", StringComparison.InvariantCultureIgnoreCase))
                {
                    List<IMessage> msg = new List<IMessage>();
                    foreach (var g in config.UndecidedQuestions.ToList())
                    {
                        config.UndecidedQuestions.Remove(g);
                        msg.Add(await _client.GetGuild(g.DecisionMSG.Guild).GetTextChannel(g.DecisionMSG.Channel).GetMessageAsync(g.DecisionMSG.MSG));
                        await (await _client.GetGuild(g.Answer.AnswerMSG.Guild).GetTextChannel(g.Answer.AnswerMSG.Channel).GetMessageAsync(g.Answer.AnswerMSG.MSG) as SocketUserMessage).ModifyAsync(k => k.Content = "Your question has been discarded by staff. This could be due to it not being an actual question or them not wanting to answer it.");
                        config.AnsweredQuestions.Add(g.Answer);
                    }
                    await _client.GetGuild(GuildID).TextChannels.FirstOrDefault(k => string.Equals(k.Name, "bot-decisions", StringComparison.InvariantCultureIgnoreCase)).DeleteMessagesAsync(msg);
                }
                config.SaveJson();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
        }
        private async Task Answer(int index, string answer = "")
        {
            try
            {
                var config = Configuration.Load();
                if (config.UndecidedQuestions.Exists(k => k.Index == index))
                {
                    var q = config.UndecidedQuestions.Find(k => k.Index == index);
                    if (string.IsNullOrEmpty(answer))
                    {
                        var a = _mind.SearchAnswer(q.Question).Answer;
                        _mind.AddAnswer(q.Question, a);
                        config.UndecidedQuestions.Remove(q);
                        await (await _client.GetGuild(q.DecisionMSG.Guild).GetTextChannel(q.DecisionMSG.Channel).GetMessageAsync(q.DecisionMSG.MSG)).DeleteAsync();
                        await (await _client.GetGuild(q.Answer.AnswerMSG.Guild).GetTextChannel(q.Answer.AnswerMSG.Channel).GetMessageAsync(q.Answer.AnswerMSG.MSG) as SocketUserMessage).ModifyAsync(k => k.Content = a);
                        config.AnsweredQuestions.Add(q.Answer);
                    }
                    else
                    {
                        _mind.AddAnswer(q.Question, answer);
                        config.UndecidedQuestions.Remove(q);
                        await (await _client.GetGuild(q.DecisionMSG.Guild).GetTextChannel(q.DecisionMSG.Channel).GetMessageAsync(q.DecisionMSG.MSG)).DeleteAsync();
                        await (await _client.GetGuild(q.Answer.AnswerMSG.Guild).GetTextChannel(q.Answer.AnswerMSG.Channel).GetMessageAsync(q.Answer.AnswerMSG.MSG) as SocketUserMessage).ModifyAsync(k => k.Content = answer);
                        config.AnsweredQuestions.Add(q.Answer);
                    }
                }
                config.SaveJson();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
        }

        private async void BotDecisionCommands(SocketCommandContext msg)
        {
            try
            {
                var command = msg.Message.Content.Split(' ').ToList();
                var comm = command[0];
                command.Remove(command[0]);
                var arguments = CheckDoubleQuotes(command);
                if (comm.StartsWith("*"))
                {
                    switch (comm.ToLower().TrimStart('*'))
                    {
                        case "d":
                            await Discard(arguments[0], msg.Guild.Id);
                            await msg.Message.DeleteAsync();
                            break;
                        case "a":
                            await Answer(int.Parse(arguments[0]), arguments[1]);
                            await msg.Message.DeleteAsync();
                            break;
                        case "y":
                            await Answer(int.Parse(arguments[0]));
                            await msg.Message.DeleteAsync();
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
        }
        private async void AdminCommands(string command, List<string> args, ulong ChannelID, ulong GuildID)
        {
            try
            {
                switch (command.ToLowerInvariant())
                {
                    case "status":
                        var d = DateTime.Now;
                        await _client.GetConnectionsAsync();
                        await _client.GetGuild(GuildID).GetTextChannel(ChannelID).SendMessageAsync("----------- Support bot V2.0 status report -----------\nPing: " + (ulong)DateTime.Now.Subtract(d).TotalMilliseconds + "ms.\nRunning on " + Environment.OSVersion + ".\nHave been running for: " + DateTime.Now.Subtract(_start) + ".\n----------- Support bot V2.0 status report -----------");
                        break;
                    case "shutdown":
                        await _client.LogoutAsync();
                        Environment.Exit(0);
                        break;
                    case "google":
                        await _client.GetGuild(GuildID).GetTextChannel(ChannelID).SendMessageAsync(WebRequest.CreateHttp(string.Format("https://www.google.com/search?q={0}", WebUtility.UrlEncode(args[0]))).GetResponse().ResponseUri.ToString());
                        break;
                    case "purge":
                        int deleted = 0;
                        if (args.Count >= 2)
                        {
                            bool i = ulong.TryParse(args[0], out ulong id);
                            if (i)
                            {
                                var u = _client.GetUser(id);
                                if (u == null)
                                {
                                    var m = await _client.GetGuild(GuildID).GetTextChannel(ChannelID).GetMessagesAsync((int)id).Flatten();
                                    await _client.GetGuild(GuildID).GetTextChannel(ChannelID).DeleteMessagesAsync(m);
                                    deleted = m.Count();
                                }
                                else
                                {
                                    var m = await _client.GetGuild(GuildID).GetTextChannel(ChannelID).GetMessagesAsync(int.Parse(args[1])).Flatten();
                                    var f = m.Where(k => k.Author.Id == u.Id);
                                    await _client.GetGuild(GuildID).GetTextChannel(ChannelID).DeleteMessagesAsync(f);
                                    deleted = f.Count();
                                }
                            }
                            else
                            {
                                var u = _client.GetGuild(GuildID).Users.ToList().Find(k =>
                                {
                                    bool? username = k?.Username?.ToLowerInvariant().Contains(args[0].ToLowerInvariant());
                                    bool? nickname = k?.Nickname?.ToLowerInvariant().Contains(args[0].ToLowerInvariant());
                                    if ((username.HasValue && username.Value) || (nickname.HasValue && nickname.Value))
                                        return true;
                                    else return false;
                                });
                                var m = await _client.GetGuild(GuildID).GetTextChannel(ChannelID).GetMessagesAsync(int.Parse(args[1])).Flatten();
                                var f = m.Where(k => k.Author.Id == u.Id);
                                await _client.GetGuild(GuildID).GetTextChannel(ChannelID).DeleteMessagesAsync(f);
                                deleted = f.Count();
                            }
                        }
                        else if (args.Count == 1)
                        {
                            bool i = ulong.TryParse(args[0], out ulong id);
                            if (i)
                            {
                                var u = _client.GetGuild(GuildID).Users.ToList().Find(k => k?.Id == id);
                                if (u == null)
                                {
                                    var m = await _client.GetGuild(GuildID).GetTextChannel(ChannelID).GetMessagesAsync((int)id).Flatten();
                                    await _client.GetGuild(GuildID).GetTextChannel(ChannelID).DeleteMessagesAsync(m);
                                    deleted = m.Count();
                                }
                                else
                                {
                                    var m = await _client.GetGuild(GuildID).GetTextChannel(ChannelID).GetMessagesAsync(100).Flatten();
                                    var f = m.Where(k => k.Author.Id == u.Id);
                                    await _client.GetGuild(GuildID).GetTextChannel(ChannelID).DeleteMessagesAsync(f);
                                    deleted = f.Count();
                                }
                            }
                            else
                            {
                                var u = _client.GetGuild(GuildID).Users.ToList().Find(k =>
                                {
                                    bool? username = k?.Username?.ToLowerInvariant().Contains(args[0].ToLowerInvariant());
                                    bool? nickname = k?.Nickname?.ToLowerInvariant().Contains(args[0].ToLowerInvariant());
                                    if ((username.HasValue && username.Value) || (nickname.HasValue && nickname.Value))
                                        return true;
                                    else return false;
                                });
                                var m = await _client.GetGuild(GuildID).GetTextChannel(ChannelID).GetMessagesAsync(100).Flatten();
                                var f = m.Where(k => k.Author.Id == u.Id);
                                await _client.GetGuild(GuildID).GetTextChannel(ChannelID).DeleteMessagesAsync(f);
                                deleted = f.Count();
                            }
                        }
                        else
                        {
                            var msgs = await _client.GetGuild(GuildID).GetTextChannel(ChannelID).GetMessagesAsync(100).Flatten();
                            await _client.GetGuild(GuildID).GetTextChannel(ChannelID).DeleteMessagesAsync(msgs);
                            deleted = msgs.Count();
                        }
                        var botms = await _client.GetGuild(GuildID).GetTextChannel(ChannelID).SendMessageAsync("Deleted " + deleted + " messages.");
                        await Task.Delay(5000);
                        await botms.DeleteAsync();
                        break;
                    case "udetails":
                        var user = _client.GetGuild(GuildID).Users.ToList().Find(k =>
                        {
                            bool? username = k?.Username?.ToLowerInvariant().Contains(args[0].ToLowerInvariant());
                            bool? nickname = k?.Nickname?.ToLowerInvariant().Contains(args[0].ToLowerInvariant());
                            if ((username.HasValue && username.Value) || (nickname.HasValue && nickname.Value))
                                return true;
                            else return false;
                        });
                        if (user == null && ulong.TryParse(args[0], out ulong ID))
                            user = _client.GetGuild(GuildID).GetUser(ID);
                        if (user != null)
                            await _client.GetGuild(GuildID).GetTextChannel(ChannelID).SendMessageAsync(user.Mention + "'s details:\nUsername - " + user.Username + "\nNickname - " + ((user.Nickname == string.Empty || user.Nickname == null) ? "N/A" : user.Nickname) + "\nID - " + user.Id + "\nStatus - " + user.Status + "\nCustom Status/Playing - " + (user.Game.HasValue ? user.Game.Value.Name : "N/A") + "\nCreated - " + user.CreatedAt + "\nJoined - " + user.JoinedAt);
                        else
                            await _client.GetGuild(GuildID).GetTextChannel(ChannelID).SendMessageAsync("User not found, did you even put a user? :thinking:");
                        break;
                    case "game":
                        await _client.SetGameAsync(args[0]);
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
        }
        private async void UserCommands(string command, List<string> args, ulong ChannelID, ulong GuildID, SocketGuildUser caller)
        {
            try
            {
                switch (command.ToLowerInvariant())
                {
                    case "flip":
                        if (_rnd.Next(0, 101) > 50)
                            await _client.GetGuild(GuildID).GetTextChannel(ChannelID).SendMessageAsync("You rolled heads.");
                        else
                            await _client.GetGuild(GuildID).GetTextChannel(ChannelID).SendMessageAsync("You rolled tails.");
                        break;
                    case "udetails":
                        try
                        {
                            await caller.SendMessageAsync("Your details:\nUsername - " + caller.Username + "\nNickname - " + (string.IsNullOrEmpty(caller.Nickname) ? "N/A" : caller.Nickname) + "\nID - " + caller.Id + "\nStatus - " + caller.Status + "\nCustom Status/Playing - " + (caller.Game.HasValue ? caller.Game.Value.Name : "N/A") + "\nCreated - " + caller.CreatedAt + "\nJoined - " + caller.JoinedAt);
                        }
                        catch (Exception)
                        {
                            if (!(await _client.GetGuild(GuildID).GetTextChannel(ChannelID).GetMessagesAsync().Flatten()).FirstOrDefault().Content.Contains("I am unable to dm you your details! Please enable public direct messaging, otherwise you can't use this command."))
                                await _client.GetGuild(GuildID).GetTextChannel(ChannelID).SendMessageAsync(caller.Mention + " I am unable to dm you your details! Please enable public direct messaging, otherwise you can't use this command.");
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
        }
        private void OwnerCommands(string command, List<string> args)
        {
            try
            {
                switch (command.ToLowerInvariant())
                {
                    case "add":
                        var config = Configuration.Load();
                        config.Pastebins.Add(new PastebinErrors() { Identifier = args[0], Answer = args[1], NameIndex = int.Parse(args[2]) });
                        config.SaveJson();
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
        }

        private List<string> CheckDoubleQuotes(List<string> Items)
        {
            List<string> result = new List<string>();
            try
            {
                string Combined = "";
                foreach (string s in Items)
                {
                    if (Combined != "")
                        Combined += " " + s;

                    if (s.StartsWith("\""))
                        Combined += s.TrimStart('"');

                    if (s.EndsWith("\""))
                    {
                        string t = Combined.TrimEnd('"');
                        result.Add(t);
                        Combined = "";
                        continue;
                    }

                    if (Combined == "")
                        result.Add(s);
                }
            }
            catch { }
            return result;
        }
        
        public async Task WriteAndExplainErrors(string PastebinURI, SocketCommandContext context)
        {
            try
            {
                var data = new List<string>();
                var regex = new Regex("^\\d(\\d|(?<!/)/)*\\d$|^\\d$");

                using (var wc = new WebClient())
                    data = wc.DownloadString(WebRequest.CreateHttp(PastebinURI.StartsWith("https://pastebin.com/raw/") ? PastebinURI : "https://pastebin.com/raw/" + PastebinURI.TrimStart('h', 't', 'p', 's', ':').TrimStart('/').TrimStart('p', 'a', 's', 't', 'e', 'b', 'i', 'n', '.', 'c', 'o', 'm').TrimStart('/')).GetResponse().ResponseUri.ToString()).Split().ToList();

                if (regex.IsMatch(data[0].TrimStart('[').TrimEnd(']')))
                {
                    List<string> errors = new List<string>();
                    string CurrentError = "";
                    foreach (string s in data)
                    {
                        if (CurrentError != string.Empty && regex.IsMatch(s.TrimStart('[').TrimEnd(']')) && s.StartsWith("["))
                        {
                            errors.Add(CurrentError);
                            CurrentError = "";
                        }

                        if (s.ToLower() == "[error]" || s.ToLower() == "[exception]")
                            CurrentError += s + " ";
                        else if (CurrentError != string.Empty)
                            CurrentError += s + " ";
                    }
                    List<string> MSGS = new List<string>() { "Errors:\n" };
                    var config = Configuration.Load();
                    foreach (string s in errors.Distinct().ToList())
                    {
                        List<string> ss = s.Split().ToList();

                        bool Found = false;
                        foreach (var sss in config.Pastebins)
                            if (s.ToLower().Contains(sss.Identifier.ToLower()))
                            {
                                MSGS.Add(string.Format(sss.Answer, sss.NameIndex > -1 ? ss[sss.NameIndex] : "") + "\n");
                                Found = true;
                                break;
                            }

                        if (!Found)
                        {
                            string a = "";
                            for (int i = 0; i < ss.Count; i++)
                                a += "[" + i + "] - " + ss[i] + "\n";
                            MSGS.Add("Unknown error: " + (s.Length > 200 ? s.Remove(200, s.Length - 200) : s) + "\n");
                            await _client.GetUser(181418012400812032).SendMessageAsync(a);
                        }
                    }
                    List<string> lines = new List<string>();
                    string currentLine = "";
                    int maxLength = 2000;
                    foreach (var currentWord in MSGS.Distinct())
                    {
                        if ((currentLine.Length > maxLength) || ((currentLine.Length + currentWord.Length) > maxLength))
                        {
                            lines.Add(currentLine);
                            currentLine = "";
                        }

                        if (currentLine.Length > 0)
                        {
                            if (currentLine.EndsWith("\n"))
                                currentLine += currentWord;
                            else
                                currentLine += " " + currentWord;
                        }
                        else
                            currentLine += currentWord;

                        if (currentWord == MSGS.Distinct().Last())
                            lines.Add(currentLine);
                    }
                    foreach (string line in lines)
                    {
                        await context.Channel.SendMessageAsync(line);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
        }
    }
}
