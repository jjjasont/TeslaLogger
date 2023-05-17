﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using uPLibrary.Networking.M2Mqtt.Messages;
using uPLibrary.Networking.M2Mqtt;

namespace TeslaLogger
{
    internal class SolarChargingOpenWB : SolarChargingBase
    {
        string host = "192.168.1.178";
        int port = 1883;
        int LP = 3;
        string ClientId = "TeslaloggerOpenWB";
        static byte[] msg1 = Encoding.ASCII.GetBytes(("1"));
        static byte[] msg0 = Encoding.ASCII.GetBytes(("0"));
        MqttClient client;

        public SolarChargingOpenWB(Car c) : base(c)
        {
            try
            {
                client = new MqttClient(host, port, false, null, null, MqttSslProtocols.None);

                client.Connect(ClientId);
                client.Subscribe(new[] {
                    $"openWB/lp/{LP}/AConfigured"
                },
                    new[] {
                        MqttMsgBase.QOS_LEVEL_AT_MOST_ONCE
                    });

                if (client.IsConnected)
                {
                    car.Log("MQTT: Connected!");
                }
                else
                {
                    car.Log("MQTT: Connection failed!");
                }

                client.MqttMsgPublishReceived += Client_MqttMsgPublishReceived;

            }
            catch (Exception ex)
            {
                car.Log(ex.ToString());
            }
        }

        private void Client_MqttMsgPublishReceived(object sender, MqttMsgPublishEventArgs e)
        {
            try
            {
                var msg = Encoding.ASCII.GetString(e.Message);

                if (e.Topic == $"openWB/lp/{LP}/AConfigured")
                {
                    int amp = int.Parse(msg);
                    SetAmpere(amp);
                }
                
            }
            catch (Exception ex)
            {
                car.Log(ex.ToString());
            }
        }

        public override void Charging(bool charging)
        {
            base.Charging(charging);
            string t = $"openWB/set/lp/{LP}/plugStat";

            client.Publish(t, charging ? msg1 : msg0);
            client.Publish($"openWB/set/lp/{LP}/chargeStat", charging ? msg1 : msg0);
        }

        public override void Plugged(bool plugged)
        {
            base.Plugged(plugged);
            client.Publish($"openWB/set/lp/{LP}/chargeStat", plugged ? msg1 : msg0);
            client.Publish($"openWB/set/lp/{LP}/boolPlugStat", plugged ? msg1 : msg0);
        }

        internal override void setPower(string charger_power, string charge_energy_added, string battery_level)
        {
            base.setPower(charger_power, charge_energy_added, battery_level);

            int Watt = int.Parse(charger_power) * 1000;

            byte[] W = Encoding.ASCII.GetBytes(Watt.ToString());
            byte[] kWh = Encoding.ASCII.GetBytes(charge_energy_added);

            client.Publish($"openWB/set/lp/{LP}/W", W);
            client.Publish($"openWB/set/lp/{LP}/kWhCounter", kWh);
        }
    }
}