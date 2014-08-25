// Copyright (c) 2010 Martin Knafve / hMailServer.com.  
// http://www.hmailserver.com

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;

namespace RegressionTests.Shared
{
   /// <summary>
   /// Summary description for ClientSocket.
   /// </summary>
   public class TcpConnection : IDisposable
   {
      private bool _useSslSocket;

      public TcpConnection()
      {

      }

      public TcpConnection(bool useSSL)
      {
         _useSslSocket = useSSL;
      }

      public TcpConnection(TcpClient client)
      {
         _tcpClient = client;
      }

      public bool IsConnected
      {
         get { return _tcpClient != null && _tcpClient.Connected; }
      }

      public bool Connect(int iPort)
      {
         return Connect(null, iPort);
      }

      private IPAddress GetHostAddress(string hostName, bool allowIPv6)
      {
         var addresses = Dns.GetHostEntry(hostName).AddressList;

         foreach (IPAddress address in addresses)
         {
            if (address.AddressFamily == AddressFamily.InterNetworkV6 && allowIPv6)
               return address;
            else if (address.AddressFamily == AddressFamily.InterNetwork)
               return address;
         }

         return null;
      }

      public bool Connect(IPAddress ipaddress, int iPort)
      {
         IPEndPoint endPoint;

         if (ipaddress != null)
            endPoint = new IPEndPoint(ipaddress, iPort);
         else
            endPoint = new IPEndPoint(GetHostAddress("localhost", false), iPort);

         try
         {
            _tcpClient = new TcpClient(endPoint.Address.ToString(), iPort);
         }

         catch
         {
            return false;
         }

         _tcpClient.Client.Blocking = true;

         if (_useSslSocket)
            if (!HandshakeAsClient())
               return false;
         
         return true;
      }

      public bool IsPortOpen(int iPort)
      {
         if (!Connect(iPort))
            return false;

         try
         {
            for (int i = 0; i < 40; i++)
            {
               if (_tcpClient.Available > 0)
                  return true;

               Thread.Sleep(25);
            }
         }
         finally
         {
            Disconnect();
         }

         return false;
      }

      public void Disconnect()
      {
         if (_useSslSocket)
            _sslStream.Close();

         _tcpClient.Close();
      }

      public bool HandshakeAsClient()
      {
         // Create an SSL stream that will close the client's stream.
         _sslStream = new SslStream(_tcpClient.GetStream(), false,
                                    ValidateServerCertificate, null);

         try
         {

            _sslStream.AuthenticateAsClient("localhost");
         }
         catch (AuthenticationException)
         {
            return false;
         }


         _useSslSocket = true;
         return true;
      }

      public bool HandshakeAsServer(X509Certificate2 certificate)
      {
         // Create an SSL stream that will close the client's stream.
         _sslStream = new SslStream(_tcpClient.GetStream(), false,
                                    ValidateServerCertificate, null);

         try
         {

            _sslStream.AuthenticateAsServer(certificate);
         }
         catch (AuthenticationException)
         {
            return false;
         }


         _useSslSocket = true;
         return true;
      }

      public bool IsSslConnection
      {
         get { return _useSslSocket; }
      }

      public string SendAndReceive(string sData)
      {
         Send(sData);
         return Receive();
      }

      public void Send(string s)
      {
         if (!_tcpClient.Connected)
            throw new InvalidOperationException("Connection closed - Unable to send data.");

         if (_useSslSocket)
         {
            var message = Encoding.UTF8.GetBytes(s);
            _sslStream.Write(message);
            _sslStream.Flush();
         }
         else
         {
            var buf = Encoding.UTF8.GetBytes(s);
            var stream = _tcpClient.GetStream();

            stream.Write(buf, 0, buf.Length);
         }
      }

      public string ReadUntil(string text)
      {
         string result = Receive();

         for (int i = 0; i < 1000; i++)
         {
            if (result.Contains(text))
               return result;

            if (!_tcpClient.Connected)
               return "";

            result += Receive();

            Thread.Sleep(10);
         }

         throw new InvalidOperationException("Timeout while waiting for server response: " + text);
      }


      public string ReadUntil(List<string> possibleReplies)
      {
         string result = Receive();

         for (int i = 0; i < 1000; i++)
         {
            foreach (string s in possibleReplies)
            {
               if (result.Contains(s))
                  return result;
            }

            Thread.Sleep(10);

            result += Receive();
         }

         throw new InvalidOperationException("Timeout while waiting for server response");
      }

      public string Receive()
      {
         var messageData = new StringBuilder();

         var buffer = new byte[2048];
         int bytesRead;

         if (_useSslSocket)
         {
            do
            {
               if (!_sslStream.CanRead)
                  return "";

               bytesRead = _sslStream.Read(buffer, 0, buffer.Length);
               Decoder decoder = Encoding.UTF8.GetDecoder();
               var chars = new char[decoder.GetCharCount(buffer, 0, bytesRead)];
               decoder.GetChars(buffer, 0, bytesRead, chars, 0);
               messageData.Append(chars);
            } while (_tcpClient.Available > 0);
         }
         else
         {
            do
            {
               var stream = _tcpClient.GetStream();

               if (!stream.CanRead)
                  return "";

               bytesRead = stream.Read(buffer, 0, buffer.Length);
               char[] chars = Encoding.ASCII.GetChars(buffer);
               var s = new string(chars, 0, bytesRead);
               messageData.Append(s);
            } while (_tcpClient.Available > 0);
         }

         return messageData.ToString();

      }

      public bool Peek()
      {
         return _tcpClient.Available > 0;
      }

      private SslStream _sslStream;
      private TcpClient _tcpClient;

      // The following method is invoked by the RemoteCertificateValidationDelegate.
      public static bool ValidateServerCertificate(
         object sender,
         X509Certificate certificate,
         X509Chain chain,
         SslPolicyErrors sslPolicyErrors)
      {
         return true;
      }

      public void Dispose()
      {
         Disconnect();
      }


      public bool TestConnect(int iPort)
      {
         bool bRetVal = Connect(iPort);
         Disconnect();
         return bRetVal;
      }


   }
}