// Copyright (c) 2010 Martin Knafve / hMailServer.com.  
// http://www.hmailserver.com

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Microsoft.VisualBasic;
using NUnit.Framework;
using RegressionTests.Shared;
using hMailServer;

namespace RegressionTests.API
{
   [TestFixture]
   public class Basics : TestFixtureBase
   {
      private static void SendMessageToTest()
      {
         var smtp = new SMTPClientSimulator();
         var recipients = new List<string>();
         recipients.Add("test@test.com");
         smtp.Send("test@test.com", recipients, "Test", "Test message");
      }

      [Test]
      public void BlowfishEncryptShouldNotRequireAdminPrivileges()
      {
         Application app = SingletonProvider<TestSetup>.Instance.GetApp();

         Account account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "test@test.com", "test");

         var newApp = new Application();
         CustomAssert.IsNotNull(newApp.Authenticate(account.Address, "test"));

         Utilities utilities = newApp.Utilities;

         string encryptedResult = utilities.BlowfishEncrypt("Test");
         CustomAssert.AreNotEqual("Test", encryptedResult, encryptedResult);

         string decrypted = utilities.BlowfishDecrypt(encryptedResult);
         CustomAssert.AreEqual("Test", decrypted, decrypted);
      }

      [Test]
      [Description("Issue 210, Duplicate UIDS when COM Messages.Add is used")]
      public void TestAddMessage()
      {
         Application app = SingletonProvider<TestSetup>.Instance.GetApp();
         Utilities utilities = app.Utilities;

         Account account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "test@test.com", "test");

         // Create a new folder.
         IMAPFolder folder = account.IMAPFolders.get_ItemByName("INBOX");
         folder.Save();

         for (int i = 0; i < 3; i++)
         {
            hMailServer.Message message = folder.Messages.Add();
            message.set_Flag(eMessageFlag.eMFSeen, true);
            message.Save();

            POP3ClientSimulator.AssertMessageCount(account.Address, "test", ((i + 1)*2) - 1);

            SMTPClientSimulator.StaticSend("test@example.com", account.Address, "Test", "Test");
            POP3ClientSimulator.AssertMessageCount(account.Address, "test", (i + 1)*2);
         }

         POP3ClientSimulator.AssertMessageCount(account.Address, "test", 6);

         var sim = new IMAPClientSimulator();
         sim.ConnectAndLogon(account.Address, "test");
         sim.SelectFolder("Inbox");

         string response = sim.Fetch("1:6 UID");

         string[] lines = Strings.Split(response, Environment.NewLine, -1, CompareMethod.Text);

         var uids = new List<string>();

         foreach (string line in lines)
         {
            int paraPos = line.IndexOf("(");
            int paraEndPos = line.IndexOf(")");

            if (paraPos < 0 || paraEndPos < 0)
               continue;

            string paraContent = line.Substring(paraPos + 1, paraEndPos - paraPos - 1);

            if (!uids.Contains(paraContent))
               uids.Add(paraContent);
         }

         CustomAssert.AreEqual(6, uids.Count);

         // Make sure the UIDS are sorted properly by creating a copy, sort the copy
         // and then compare to original.
         var copy = new List<string>();
         copy.InsertRange(0, uids);
         copy.Sort();

         CustomAssert.AreEqual(copy, uids);
      }


      [Test]
      [Description("Add text to an empty body during sending of attachments")]
      public void TestAddTextToEmptyBody()
      {
         Account account1 = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "test@test.com", "test");

         // Send a message to the account.
         string messageText = @"Date: Thu, 03 Jul 2008 22:01:53 +0200\r\n" +
                              "From: Test <test@test.com>\r\n" +
                              "MIME-Version: 1.0\r\n" +
                              "To: test@test.com\r\n" +
                              "Subject: test\r\n" +
                              "Content-Type: multipart/mixed;\r\n" +
                              "  boundary=\"------------050908050500020808050006\"\r\n" +
                              "\r\n" +
                              "This is a multi-part message in MIME format.\r\n" +
                              "--------------050908050500020808050006\r\n" +
                              "Content-Type: text/plain; charset=ISO-8859-1; format=flowed\r\n" +
                              "Content-Transfer-Encoding: 7bit\r\n" +
                              "\r\n" +
                              "Test\r\n" +
                              "\r\n" +
                              "--------------050908050500020808050006\r\n" +
                              "Content-Type: text/plain;\r\n" +
                              " name=\"AUTOEXEC.BAT\"\r\n" +
                              "Content-Transfer-Encoding: base64\r\n" +
                              "Content-Disposition: inline;\r\n" +
                              " filename=\"AUTOEXEC.BAT\"\r\n" +
                              "\r\n" +
                              "\r\n" +
                              "--------------050908050500020808050006--\r\n";

         SMTPClientSimulator.StaticSendRaw("test@test.com", "test@test.com", messageText);

         hMailServer.Message message =
            TestSetup.AssertRetrieveFirstMessage(account1.IMAPFolders.get_ItemByName("INBOX"));
         CustomAssert.AreEqual(1, message.Attachments.Count);
         CustomAssert.AreEqual("AUTOEXEC.BAT", message.Attachments[0].Filename);
      }

      [Test]
      [Description("Issue 210, Duplicate UIDS when COM Messages.Add is used")]
      public void TestCopyMessage()
      {
         Application app = SingletonProvider<TestSetup>.Instance.GetApp();
         Utilities utilities = app.Utilities;

         Account account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "test@test.com", "test");

         // Create a new folder.
         IMAPFolder folder = account.IMAPFolders.get_ItemByName("INBOX");
         folder.Save();

         IMAPFolder someOtherFolder = account.IMAPFolders.Add("SomeOtherFolder");

         for (int i = 0; i < 3; i++)
         {
            hMailServer.Message message = folder.Messages.Add();
            message.set_Flag(eMessageFlag.eMFSeen, true);
            message.Save();

            message.Copy(someOtherFolder.ID);
         }

         SMTPClientSimulator.StaticSend("test@example.com", account.Address, "Test", "Test");

         // Copy back to inbox.
         for (int i = 0; i < 3; i ++)
         {
            hMailServer.Message message = someOtherFolder.Messages[i];
            message.Copy(folder.ID);
         }

         POP3ClientSimulator.AssertMessageCount(account.Address, "test", 7);

         var sim = new IMAPClientSimulator();
         sim.ConnectAndLogon(account.Address, "test");
         sim.SelectFolder("Inbox");
         string response = sim.Fetch("1:7 UID");

         string[] lines = Strings.Split(response, Environment.NewLine, -1, CompareMethod.Text);

         var uids = new List<string>();

         foreach (string line in lines)
         {
            int paraPos = line.IndexOf("(");
            int paraEndPos = line.IndexOf(")");

            if (paraPos < 0 || paraEndPos < 0)
               continue;

            string paraContent = line.Substring(paraPos + 1, paraEndPos - paraPos - 1);

            if (!uids.Contains(paraContent))
               uids.Add(paraContent);
         }

         CustomAssert.AreEqual(7, uids.Count);

         // Make sure the UIDS are sorted properly by creating a copy, sort the copy
         // and then compare to original.
         var copy = new List<string>();
         copy.InsertRange(0, uids);
         copy.Sort();

         CustomAssert.AreEqual(copy, uids);
      }

      [Test]
      public void TestCriteriaMatching()
      {
         Application app = SingletonProvider<TestSetup>.Instance.GetApp();
         Utilities utilities = app.Utilities;

         CustomAssert.IsTrue(utilities.CriteriaMatch("Test", eRuleMatchType.eMTEquals, "Test"));
         CustomAssert.IsFalse(utilities.CriteriaMatch("Testa", eRuleMatchType.eMTEquals, "Test"));

         CustomAssert.IsTrue(utilities.CriteriaMatch("Test*", eRuleMatchType.eMTWildcard, "Testar!"));
         CustomAssert.IsFalse(utilities.CriteriaMatch("Test*", eRuleMatchType.eMTWildcard, "Tesb"));
      }

      [Test]
      public void TestDomainDeletion()
      {
         Application app = SingletonProvider<TestSetup>.Instance.GetApp();

         CustomAssert.IsNotNull(app.Links.get_Domain(_domain.ID));

         app.Domains.DeleteByDBID(_domain.ID);

         try
         {
            app.Links.get_Domain(_domain.ID);

            CustomAssert.Fail("Didn't throw");
         }
         catch (Exception)
         {
            // should throw => ok
         }
      }

      [Test]
      public void TestEventLog()
      {
         Application application = SingletonProvider<TestSetup>.Instance.GetApp();

         // First set up a script
         string script =
            @"Sub OnAcceptMessage(oClient, oMessage)
                               EventLog.Write(""HOWDY"")
                              End Sub";

         Scripting scripting = _settings.Scripting;
         string file = scripting.CurrentScriptFile;
         TestSetup.WriteFile(file, script);
         scripting.Enabled = true;
         scripting.Reload();

         // Drop the current event log
         string eventLogFile = _settings.Logging.CurrentEventLog;

         SingletonProvider<TestSetup>.Instance.DeleteEventLog();

         // Add an account and send a message to it.
         Account oAccount1 = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "test@test.com", "test");

         SendMessageToTest();

         POP3ClientSimulator.AssertGetFirstMessageText(oAccount1.Address, "test");

         TestSetup.AssertFileExists(eventLogFile, false);

         // Check that it starts with Unicode markers.
         for (int i = 0; i <= 400; i++)
         {
            try
            {
               var fs = new FileStream(eventLogFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
               var br = new BinaryReader(fs);
               int i1 = br.ReadByte();
               int i2 = br.ReadByte();
               br.Close();
               fs.Close();

               CustomAssert.AreEqual(255, i1);
               CustomAssert.AreEqual(254, i2);

               break;
            }
            catch (Exception e)
            {
               if (i == 40)
                  throw e;
            }

            Thread.Sleep(25);
         }
      }

      [Test]
      [Category("COM API")]
      [Description("Test deletion of IMAP folders using COM API.")]
      public void TestFolderDeletion()
      {
         // Create a test account
         // Fetch the default domain
         Account account1 = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "folder@test.com", "test");
         IMAPFolder folder = account1.IMAPFolders.Add("TestFolder1");
         folder.Save();

         var simulator1 = new IMAPClientSimulator();
         simulator1.ConnectAndLogon(account1.Address, "test");
         string result = simulator1.List();
         CustomAssert.IsTrue(result.Contains(folder.Name));
         simulator1.Disconnect();

         // Delete the folder and confirm it's no longer listed.
         folder.Delete();

         simulator1.ConnectAndLogon(account1.Address, "test");
         result = simulator1.List();
         CustomAssert.IsFalse(result.Contains(folder.Name));
         simulator1.Disconnect();
      }

      [Test]
      public void TestIMAPFolderPermissionAccessGroup()
      {
         Application application = SingletonProvider<TestSetup>.Instance.GetApp();


         Account account1 = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "account1@test.com", "test");
         Group group = SingletonProvider<TestSetup>.Instance.AddGroup("TestGroup");

         IMAPFolders publicFolders = _settings.PublicFolders;
         IMAPFolder folder = publicFolders.Add("Share1");
         folder.Save();

         IMAPFolderPermission permission = folder.Permissions.Add();
         permission.PermissionGroupID = group.ID;
         permission.PermissionType = eACLPermissionType.ePermissionTypeGroup;
         permission.Save();

         CustomAssert.AreEqual(permission.Group.Name, group.Name);

         permission = folder.Permissions.Add();
         permission.PermissionAccountID = account1.ID;
         permission.PermissionType = eACLPermissionType.ePermissionTypeUser;
         permission.Save();

         CustomAssert.AreEqual(permission.Account.Address, account1.Address);
      }

      [Test]
      public void TestInternalDateCombinedWithOnDeliverMessage()
      {
         Application application = SingletonProvider<TestSetup>.Instance.GetApp();
         Scripting scripting = _settings.Scripting;
         scripting.Language = "JScript";
         // First set up a script
         string script =
            @"function OnDeliverMessage(oMessage)
                           {
                               EventLog.Write(oMessage.InternalDate);
                           }";


         string file = scripting.CurrentScriptFile;
         TestSetup.WriteFile(file, script);
         scripting.Enabled = true;
         scripting.Reload();

         // Add an account and send a message to it.
         Account oAccount1 = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "test@test.com", "test");

         SMTPClientSimulator.StaticSend(oAccount1.Address, oAccount1.Address, "Test", "SampleBody");

         POP3ClientSimulator.AssertMessageCount(oAccount1.Address, "test", 1);
         string text = TestSetup.ReadExistingTextFile(_settings.Logging.CurrentEventLog);

         string[] columns = text.Split('\t');

         if (columns.Length != 3)
            CustomAssert.Fail("Wrong number of cols: " + text);

         string lastColumn = columns[columns.Length - 1];

         CustomAssert.IsFalse(lastColumn.Contains("00:00:00"), lastColumn);
         CustomAssert.IsTrue(lastColumn.Contains(DateTime.Now.Year.ToString()), lastColumn);
         CustomAssert.IsTrue(lastColumn.Contains(DateTime.Now.Month.ToString()), lastColumn);
         CustomAssert.IsTrue(lastColumn.Contains(DateTime.Now.Day.ToString()), lastColumn);
      }


      [Test]
      [Description("Test that live log works and that it's reset when enabled.")]
      public void TestLiveLog()
      {
         Application app = SingletonProvider<TestSetup>.Instance.GetApp();
         Utilities utilities = app.Utilities;

         Logging logging = app.Settings.Logging;

         logging.EnableLiveLogging(true);

         // Add an account and send a message to it.
         Account account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "test@test.com", "test");

         SMTPClientSimulator.StaticSend(account.Address, account.Address, "Test", "SampleBody");
         POP3ClientSimulator.AssertMessageCount(account.Address, "test", 1);

         string liveLog = logging.LiveLog;
         CustomAssert.IsTrue(liveLog.Length > 0, liveLog);

         SMTPClientSimulator.StaticSend(account.Address, account.Address, "Test", "SampleBody");
         POP3ClientSimulator.AssertMessageCount(account.Address, "test", 2);

         logging.EnableLiveLogging(true);

         liveLog = logging.LiveLog;
         CustomAssert.IsFalse(liveLog.Contains("SMTPDeliverer - Message"));
      }

      [Test]
      public void TestReinitialize()
      {
         string @messageText =
            "From: test@test.com\r\n" +
            "\r\n" +
            "WhatTest\r\n";

         Account account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "test@test.com", "test");
         CustomAssert.IsTrue(SMTPClientSimulator.StaticSend(account.Address, account.Address, "First message",
                                                      "Test message"));
         POP3ClientSimulator.AssertMessageCount(account.Address, "test", 1);

         // Create another message on disk and import it.
         string domainPath = Path.Combine(_application.Settings.Directories.DataDirectory, "test.com");
         string accountPath = Path.Combine(domainPath, "test");
         Directory.CreateDirectory(accountPath);
         string fileName = Path.Combine(accountPath, "something.eml");
         File.WriteAllText(fileName, messageText);
         CustomAssert.IsTrue(_application.Utilities.ImportMessageFromFile(fileName, account.ID));

         // Since the cache isn't refreshed, the message has not yet appeared.
         POP3ClientSimulator.AssertMessageCount(account.Address, "test", 1);

         // Reinitialize the server. Should, among other things, clear the cache.
         _application.Reinitialize();

         // Now the message should have appeared.
         POP3ClientSimulator.AssertMessageCount(account.Address, "test", 2);

         var sim = new POP3ClientSimulator();
         sim.ConnectAndLogon(account.Address, "test");
         messageText = sim.RETR(2);
         sim.QUIT();

         CustomAssert.IsTrue(messageText.Contains("WhatTest"), messageText);
      }

      [Test]
      public void TestRetrieveMessageID()
      {
         Application app = SingletonProvider<TestSetup>.Instance.GetApp();
         Utilities utilities = app.Utilities;

         // Add an account and send a message to it.
         Account account = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "test@test.com", "test");

         SMTPClientSimulator.StaticSend(account.Address, account.Address, "Test", "SampleBody");
         POP3ClientSimulator.AssertMessageCount(account.Address, "test", 1);

         hMailServer.Message message = account.IMAPFolders.get_ItemByName("INBOX").Messages[0];

         CustomAssert.AreEqual(message.ID, utilities.RetrieveMessageID(message.Filename));
         CustomAssert.AreEqual(0, utilities.RetrieveMessageID(@"C:\some\nonexistant\file"));
      }

      [Test]
      [Description("Issue 368, Routes.ItemByName returns invalid object")]
      [ExpectedException(ExpectedMessage = "Invalid index.", MatchType = MessageMatch.Contains)]
      public void TestRetrieveNonexistantRoute()
      {
         Settings settings = SingletonProvider<TestSetup>.Instance.GetApp().Settings;
         settings.Routes.get_ItemByName("whatever.com");
      }

      [Test]
      public void TestSaveMessageInExistingIMAPFolder()
      {
         Settings settings = SingletonProvider<TestSetup>.Instance.GetApp().Settings;

         Account oAccount1 = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "test@test.com", "test");

         // Check that the message does not exist
         POP3ClientSimulator.AssertMessageCount(oAccount1.Address, "test", 0);

         // Send a message to the account.
         IMAPFolder folder = oAccount1.IMAPFolders.get_ItemByName("INBOX");

         hMailServer.Message oMessage = folder.Messages.Add();

         CustomAssert.AreEqual(0, oMessage.State);

         oMessage.Body = "Všlkommen till verkligheten";
         oMessage.Subject = "Hej";
         oMessage.Save();

         CustomAssert.AreEqual(2, oMessage.State);
         CustomAssert.IsFalse(oMessage.Filename.Contains(settings.PublicFolderDiskName));
         CustomAssert.IsTrue(oMessage.Filename.Contains(_domain.Name));

         // Check that the message exists
         string message = POP3ClientSimulator.AssertGetFirstMessageText(oAccount1.Address, "test");

         CustomAssert.IsNotEmpty(message);
         CustomAssert.Less(0, message.IndexOf("Hej"));
      }

      [Test]
      public void TestSaveMessageInPublicIMAPFolder()
      {
         Settings settings = SingletonProvider<TestSetup>.Instance.GetApp().Settings;
         IMAPFolders publicFolders = settings.PublicFolders;

         IMAPFolder testFolder = publicFolders.Add("TestFolder");
         testFolder.Save();

         // Send a message to the account.
         hMailServer.Message oMessage = testFolder.Messages.Add();

         CustomAssert.AreEqual(0, oMessage.State);

         oMessage.Body = "Všlkommen till verkligheten";
         oMessage.Subject = "Hej";
         oMessage.Save();

         CustomAssert.AreEqual(2, oMessage.State);
         CustomAssert.IsTrue(oMessage.Filename.Contains(settings.PublicFolderDiskName));
         CustomAssert.IsFalse(oMessage.Filename.Contains(_domain.Name));
      }

      [Test]
      public void TestSendMessage()
      {
         Account oAccount1 = SingletonProvider<TestSetup>.Instance.AddAccount(_domain, "test@test.com", "test");

         // Send a message to the account.
         var oMessage = new hMailServer.Message();

         CustomAssert.AreEqual(0, oMessage.State);

         oMessage.AddRecipient("Martin", oAccount1.Address);
         oMessage.Body = "Všlkommen till verkligheten";
         oMessage.Subject = "Hej";
         oMessage.Save();

         CustomAssert.AreEqual(1, oMessage.State);

         // Check that the message exists
         string message = POP3ClientSimulator.AssertGetFirstMessageText(oAccount1.Address, "test");

         CustomAssert.IsNotEmpty(message);
         CustomAssert.Less(0, message.IndexOf("Hej"));
      }
   }
}