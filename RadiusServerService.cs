﻿using Flexinets.MobileData.SMS;
using Flexinets.Radius.Core;
using Flexinets.Radius.Disconnector;
using Flexinets.Radius.PacketHandlers;
using Flexinets.Security;
using FlexinetsDBEF;
using log4net;
using Microsoft.Azure;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.ServiceProcess;

namespace Flexinets.Radius
{
    public partial class RadiusServerService : ServiceBase
    {
        private FlexinetsEntitiesFactory _contextFactory;
        private RadiusServer _authenticationServer;
        private RadiusServer _accountingServer;
        private readonly ILog _log = LogManager.GetLogger(typeof(RadiusServerService));


        public RadiusServerService()
        {
            InitializeComponent();
        }

        protected override void OnStart(string[] args)
        {
            log4net.Config.XmlConfigurator.Configure();
            try
            {
                _log.Info($"Starting RadiusServerService build version {FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location).FileVersion}");
                _log.Info("Reading configuration");
                _contextFactory = new FlexinetsEntitiesFactory(CloudConfigurationManager.GetSetting("SQLConnectionString"));
                var path = Path.GetDirectoryName(AppDomain.CurrentDomain.BaseDirectory) + "\\radius.dictionary";
                var dictionary = new RadiusDictionary(path);
                var port = Convert.ToInt32(CloudConfigurationManager.GetSetting("Port"));
                var ipassSecret = CloudConfigurationManager.GetSetting("ipasssecret");
                var mbbSecret = CloudConfigurationManager.GetSetting("mbbsecret");
                var mbbNewSecret = CloudConfigurationManager.GetSetting("mbbnewsecret");
                var disconnectSecret = CloudConfigurationManager.GetSetting("disconnectSecret");
                var apiUrl = CloudConfigurationManager.GetSetting("ApiUrl");
                var checkPathOld = CloudConfigurationManager.GetSetting("ipass.checkpathold");
                var checkPathNew = CloudConfigurationManager.GetSetting("ipass.checkpathnew");
                _log.Info("Configuration read");

                var authProxy = new iPassAuthenticationProxy(_contextFactory, checkPathOld, checkPathNew);


                _authenticationServer = new RadiusServer(new IPEndPoint(IPAddress.Any, port), dictionary, RadiusServerType.Authentication);
                _accountingServer = new RadiusServer(new IPEndPoint(IPAddress.Any, port + 1), dictionary, RadiusServerType.Accounting);    // todo, good grief...

                var ipassPacketHandler = new iPassPacketHandler(_contextFactory, authProxy, new UserAuthenticationProvider(null, _contextFactory, null));
                _authenticationServer.AddPacketHandler(IPAddress.Parse("127.0.0.1"), ipassSecret, ipassPacketHandler);
                _accountingServer.AddPacketHandler(IPAddress.Parse("127.0.0.1"), ipassSecret, ipassPacketHandler);

                var smsgateway = new SMSGatewayTwilio(
                    CloudConfigurationManager.GetSetting("twilio.deliveryreporturl"),
                    CloudConfigurationManager.GetSetting("twilio.accountsid"),
                    CloudConfigurationManager.GetSetting("twilio.authtoken"));

                var welcomeSender = new WelcomeSender(_contextFactory, smsgateway);
                var disconnectorV2 = new RadiusDisconnectorV2(
                    CloudConfigurationManager.GetSetting("disconnector.username"),
                    CloudConfigurationManager.GetSetting("disconnector.password"),
                    CloudConfigurationManager.GetSetting("disconnector.apiurl"));
                var mbbPacketHandlerV2 = new MobileDataPacketHandlerV2(_contextFactory, welcomeSender, disconnectorV2);

                // todo refactor this
                var remoteAddresses = new List<IPAddress> {
                    IPAddress.Parse("10.239.24.6"),
                    IPAddress.Parse("10.239.24.7"),
                    IPAddress.Parse("10.239.24.8") };

                _authenticationServer.AddPacketHandler(remoteAddresses, mbbNewSecret, mbbPacketHandlerV2);
                _accountingServer.AddPacketHandler(remoteAddresses, mbbNewSecret, mbbPacketHandlerV2);

                _authenticationServer.Start();
                _accountingServer.Start();
            }
            catch (Exception ex)
            {
                _log.Fatal("Failed to start service", ex);
                throw;
            }
        }

        protected override void OnStop()
        {
            _authenticationServer?.Stop();
            _authenticationServer?.Dispose();
            _accountingServer?.Stop();
            _accountingServer?.Dispose();
        }
    }
}
