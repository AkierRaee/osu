// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using System.Collections.Generic;
using System.Net;
using NUnit.Framework;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Logging;
using osu.Framework.Testing;
using osu.Framework.Utils;
using osu.Game.Graphics.UserInterface;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Online.Chat;
using osu.Game.Overlays;
using osu.Game.Overlays.Chat;
using osu.Game.Overlays.Chat.Listing;
using osu.Game.Overlays.Chat.ChannelList;
using osuTK.Input;

namespace osu.Game.Tests.Visual.Online
{
    [TestFixture]
    public class TestSceneChatOverlayV2 : OsuManualInputManagerTestScene
    {
        private ChatOverlayV2 chatOverlay;
        private ChannelManager channelManager;

        private readonly APIUser testUser;
        private readonly Channel testPMChannel;
        private readonly Channel[] testChannels;
        private Channel testChannel1 => testChannels[0];
        private Channel testChannel2 => testChannels[1];

        public TestSceneChatOverlayV2()
        {
            testUser = new APIUser { Username = "test user", Id = 5071479 };
            testPMChannel = new Channel(testUser);
            testChannels = Enumerable.Range(1, 10).Select(createPublicChannel).ToArray();
        }

        [SetUp]
        public void SetUp() => Schedule(() =>
        {
            Child = new DependencyProvidingContainer
            {
                RelativeSizeAxes = Axes.Both,
                CachedDependencies = new (Type, object)[]
                {
                    (typeof(ChannelManager), channelManager = new ChannelManager()),
                },
                Children = new Drawable[]
                {
                    channelManager,
                    chatOverlay = new ChatOverlayV2 { RelativeSizeAxes = Axes.Both },
                },
            };
        });

        [SetUpSteps]
        public void SetUpSteps()
        {
            AddStep("Setup request handler", () =>
            {
                ((DummyAPIAccess)API).HandleRequest = req =>
                {
                    switch (req)
                    {
                        case GetUpdatesRequest getUpdates:
                            getUpdates.TriggerFailure(new WebException());
                            return true;

                        case JoinChannelRequest joinChannel:
                            joinChannel.TriggerSuccess();
                            return true;

                        case LeaveChannelRequest leaveChannel:
                            leaveChannel.TriggerSuccess();
                            return true;

                        case GetMessagesRequest getMessages:
                            getMessages.TriggerSuccess(createChannelMessages(getMessages.Channel));
                            return true;

                        case GetUserRequest getUser:
                            if (getUser.Lookup == testUser.Username)
                                getUser.TriggerSuccess(testUser);
                            else
                                getUser.TriggerFailure(new WebException());
                            return true;

                        case PostMessageRequest postMessage:
                            postMessage.TriggerSuccess(new Message(RNG.Next(0, 10000000))
                            {
                                Content = postMessage.Message.Content,
                                ChannelId = postMessage.Message.ChannelId,
                                Sender = postMessage.Message.Sender,
                                Timestamp = new DateTimeOffset(DateTime.Now),
                            });
                            return true;

                        default:
                            Logger.Log($"Unhandled Request Type: {req.GetType()}");
                            return false;
                    }
                };
            });

            AddStep("Add test channels", () =>
            {
                (channelManager.AvailableChannels as BindableList<Channel>)?.AddRange(testChannels);
            });
        }

        [Test]
        public void TestShowHide()
        {
            AddStep("Show overlay", () => chatOverlay.Show());
            AddAssert("Overlay is visible", () => chatOverlay.State.Value == Visibility.Visible);
            AddStep("Hide overlay", () => chatOverlay.Hide());
            AddAssert("Overlay is hidden", () => chatOverlay.State.Value == Visibility.Hidden);
        }

        [Test]
        public void TestChannelSelection()
        {
            AddStep("Show overlay", () => chatOverlay.Show());
            AddAssert("Listing is visible", () => listingVisibility == Visibility.Visible);
            AddStep("Join channel 1", () => channelManager.JoinChannel(testChannel1));
            AddStep("Select channel 1", () => clickDrawable(getChannelListItem(testChannel1)));
            AddAssert("Listing is hidden", () => listingVisibility == Visibility.Hidden);
            AddAssert("Loading is hidden", () => loadingVisibility == Visibility.Hidden);
            AddAssert("Current channel is correct", () => channelManager.CurrentChannel.Value == testChannel1);
            AddAssert("DrawableChannel is correct", () => currentDrawableChannel.Channel == testChannel1);
        }

        [Test]
        public void TestSearchInListing()
        {
            AddStep("Show overlay", () => chatOverlay.Show());
            AddAssert("Listing is visible", () => listingVisibility == Visibility.Visible);
            AddStep("Search for 'number 2'", () => chatOverlayTextBox.Text = "number 2");
            AddUntilStep("Only channel 2 visibile", () =>
            {
                IEnumerable<ChannelListingItem> listingItems = chatOverlay.ChildrenOfType<ChannelListingItem>()
                                                                          .Where(item => item.IsPresent);
                return listingItems.Count() == 1 && listingItems.Single().Channel == testChannel2;
            });
        }

        [Test]
        public void TestChannelCloseButton()
        {
            AddStep("Show overlay", () => chatOverlay.Show());
            AddStep("Join PM and public channels", () =>
            {
                channelManager.JoinChannel(testChannel1);
                channelManager.JoinChannel(testPMChannel);
            });
            AddStep("Select PM channel", () => clickDrawable(getChannelListItem(testPMChannel)));
            AddStep("Click close button", () =>
            {
                ChannelListItemCloseButton closeButton = getChannelListItem(testPMChannel).ChildrenOfType<ChannelListItemCloseButton>().Single();
                clickDrawable(closeButton);
            });
            AddAssert("PM channel closed", () => !channelManager.JoinedChannels.Contains(testPMChannel));
            AddStep("Select normal channel", () => clickDrawable(getChannelListItem(testChannel1)));
            AddStep("Click close button", () =>
            {
                ChannelListItemCloseButton closeButton = getChannelListItem(testChannel1).ChildrenOfType<ChannelListItemCloseButton>().Single();
                clickDrawable(closeButton);
            });
            AddAssert("Normal channel closed", () => !channelManager.JoinedChannels.Contains(testChannel1));
        }

        [Test]
        public void TestChatCommand()
        {
            AddStep("Show overlay", () => chatOverlay.Show());
            AddStep("Join channel 1", () => channelManager.JoinChannel(testChannel1));
            AddStep("Select channel 1", () => clickDrawable(getChannelListItem(testChannel1)));
            AddStep("Open chat with user", () => channelManager.PostCommand($"chat {testUser.Username}"));
            AddAssert("PM channel is selected", () =>
                channelManager.CurrentChannel.Value.Type == ChannelType.PM && channelManager.CurrentChannel.Value.Users.Single() == testUser);
            AddStep("Open chat with non-existent user", () => channelManager.PostCommand("chat user_doesnt_exist"));
            AddAssert("Last message is error", () => channelManager.CurrentChannel.Value.Messages.Last() is ErrorMessage);

            // Make sure no unnecessary requests are made when the PM channel is already open.
            AddStep("Select channel 1", () => clickDrawable(getChannelListItem(testChannel1)));
            AddStep("Unregister request handling", () => ((DummyAPIAccess)API).HandleRequest = null);
            AddStep("Open chat with user", () => channelManager.PostCommand($"chat {testUser.Username}"));
            AddAssert("PM channel is selected", () =>
                channelManager.CurrentChannel.Value.Type == ChannelType.PM && channelManager.CurrentChannel.Value.Users.Single() == testUser);
        }

        [Test]
        public void TestMultiplayerChannelIsNotShown()
        {
            Channel multiplayerChannel = null;

            AddStep("Show overlay", () => chatOverlay.Show());
            AddStep("Join multiplayer channel", () => channelManager.JoinChannel(multiplayerChannel = new Channel(new APIUser())
            {
                Name = "#mp_1",
                Type = ChannelType.Multiplayer,
            }));
            AddAssert("Channel is joined", () => channelManager.JoinedChannels.Contains(multiplayerChannel));
            AddUntilStep("Channel not present in listing", () => !chatOverlay.ChildrenOfType<ChannelListingItem>()
                                                                             .Where(item => item.IsPresent)
                                                                             .Select(item => item.Channel)
                                                                             .Contains(multiplayerChannel));
        }

        [Test]
        public void TestHighlightOnCurrentChannel()
        {
            Message message = null;

            AddStep("Show overlay", () => chatOverlay.Show());
            AddStep("Join channel 1", () => channelManager.JoinChannel(testChannel1));
            AddStep("Select channel 1", () => clickDrawable(getChannelListItem(testChannel1)));
            AddStep("Send message in channel 1", () =>
            {
                testChannel1.AddNewMessages(message = new Message
                {
                    ChannelId = testChannel1.Id,
                    Content = "Message to highlight!",
                    Timestamp = DateTimeOffset.Now,
                    Sender = testUser,
                });
            });
            AddStep("Highlight message", () => chatOverlay.HighlightMessage(message, testChannel1));
        }

        [Test]
        public void TestHighlightOnAnotherChannel()
        {
            Message message = null;

            AddStep("Show overlay", () => chatOverlay.Show());
            AddStep("Join channel 1", () => channelManager.JoinChannel(testChannel1));
            AddStep("Join channel 2", () => channelManager.JoinChannel(testChannel2));
            AddStep("Select channel 1", () => clickDrawable(getChannelListItem(testChannel1)));
            AddStep("Send message in channel 2", () =>
            {
                testChannel2.AddNewMessages(message = new Message
                {
                    ChannelId = testChannel2.Id,
                    Content = "Message to highlight!",
                    Timestamp = DateTimeOffset.Now,
                    Sender = testUser,
                });
            });
            AddStep("Highlight message", () => chatOverlay.HighlightMessage(message, testChannel2));
            AddAssert("Channel 2 is selected", () => channelManager.CurrentChannel.Value == testChannel2);
            AddAssert("Channel 2 is visible", () => currentDrawableChannel.Channel == testChannel2);
        }

        [Test]
        public void TestHighlightOnLeftChannel()
        {
            Message message = null;

            AddStep("Show overlay", () => chatOverlay.Show());
            AddStep("Join channel 1", () => channelManager.JoinChannel(testChannel1));
            AddStep("Join channel 2", () => channelManager.JoinChannel(testChannel2));
            AddStep("Select channel 1", () => clickDrawable(getChannelListItem(testChannel1)));
            AddStep("Send message in channel 2", () =>
            {
                testChannel2.AddNewMessages(message = new Message
                {
                    ChannelId = testChannel2.Id,
                    Content = "Message to highlight!",
                    Timestamp = DateTimeOffset.Now,
                    Sender = testUser,
                });
            });
            AddStep("Leave channel 2", () => channelManager.LeaveChannel(testChannel2));
            AddStep("Highlight message", () => chatOverlay.HighlightMessage(message, testChannel2));
            AddAssert("Channel 2 is selected", () => channelManager.CurrentChannel.Value == testChannel2);
            AddAssert("Channel 2 is visible", () => currentDrawableChannel.Channel == testChannel2);
        }

        [Test]
        public void TestHighlightWhileChatNeverOpen()
        {
            Message message = null;

            AddStep("Join channel 1", () => channelManager.JoinChannel(testChannel1));
            AddStep("Send message in channel 1", () =>
            {
                testChannel1.AddNewMessages(message = new Message
                {
                    ChannelId = testChannel1.Id,
                    Content = "Message to highlight!",
                    Timestamp = DateTimeOffset.Now,
                    Sender = testUser,
                });
            });
            AddStep("Highlight message", () => chatOverlay.HighlightMessage(message, testChannel1));
        }

        [Test]
        public void TestHighlightWithNullChannel()
        {
            Message message = null;

            AddStep("Join channel 1", () => channelManager.JoinChannel(testChannel1));
            AddStep("Send message in channel 1", () =>
            {
                testChannel1.AddNewMessages(message = new Message
                {
                    ChannelId = testChannel1.Id,
                    Content = "Message to highlight!",
                    Timestamp = DateTimeOffset.Now,
                    Sender = testUser,
                });
            });
            AddStep("Set null channel", () => channelManager.CurrentChannel.Value = null);
            AddStep("Highlight message", () => chatOverlay.HighlightMessage(message, testChannel1));
        }

        [Test]
        public void TextBoxRetainsFocus()
        {
            AddStep("Show overlay", () => chatOverlay.Show());
            AddAssert("TextBox is focused", () => InputManager.FocusedDrawable == chatOverlayTextBox);
            AddStep("Join channel 1", () => channelManager.JoinChannel(testChannel1));
            AddStep("Select channel 1", () => clickDrawable(getChannelListItem(testChannel1)));
            AddAssert("TextBox is focused", () => InputManager.FocusedDrawable == chatOverlayTextBox);
            AddStep("Click selector", () => clickDrawable(chatOverlay.ChildrenOfType<ChannelListSelector>().Single()));
            AddAssert("TextBox is focused", () => InputManager.FocusedDrawable == chatOverlayTextBox);
            AddStep("Click listing", () => clickDrawable(chatOverlay.ChildrenOfType<ChannelListing>().Single()));
            AddAssert("TextBox is focused", () => InputManager.FocusedDrawable == chatOverlayTextBox);
            AddStep("Click drawable channel", () => clickDrawable(chatOverlay.ChildrenOfType<DrawableChannel>().Single()));
            AddAssert("TextBox is focused", () => InputManager.FocusedDrawable == chatOverlayTextBox);
            AddStep("Click channel list", () => clickDrawable(chatOverlay.ChildrenOfType<ChannelList>().Single()));
            AddAssert("TextBox is focused", () => InputManager.FocusedDrawable == chatOverlayTextBox);
            AddStep("Click top bar", () => clickDrawable(chatOverlay.ChildrenOfType<ChatOverlayTopBar>().Single()));
            AddAssert("TextBox is focused", () => InputManager.FocusedDrawable == chatOverlayTextBox);
            AddStep("Hide overlay", () => chatOverlay.Hide());
            AddAssert("TextBox is not focused", () => InputManager.FocusedDrawable == null);
        }

        private Visibility listingVisibility =>
            chatOverlay.ChildrenOfType<ChannelListing>().Single().State.Value;

        private Visibility loadingVisibility =>
            chatOverlay.ChildrenOfType<LoadingLayer>().Single().State.Value;

        private DrawableChannel currentDrawableChannel =>
            chatOverlay.ChildrenOfType<Container<DrawableChannel>>().Single().Child;

        private ChannelListItem getChannelListItem(Channel channel) =>
            chatOverlay.ChildrenOfType<ChannelListItem>().Single(item => item.Channel == channel);

        private ChatTextBox chatOverlayTextBox =>
            chatOverlay.ChildrenOfType<ChatTextBox>().Single();

        private void clickDrawable(Drawable d)
        {
            InputManager.MoveMouseTo(d);
            InputManager.Click(MouseButton.Left);
        }

        private List<Message> createChannelMessages(Channel channel)
        {
            var message = new Message
            {
                ChannelId = channel.Id,
                Content = $"Hello, this is a message in {channel.Name}",
                Sender = testUser,
                Timestamp = new DateTimeOffset(DateTime.Now),
            };
            return new List<Message> { message };
        }

        private Channel createPublicChannel(int id) => new Channel
        {
            Id = id,
            Name = $"#channel-{id}",
            Topic = $"We talk about the number {id} here",
            Type = ChannelType.Public,
        };
    }
}
