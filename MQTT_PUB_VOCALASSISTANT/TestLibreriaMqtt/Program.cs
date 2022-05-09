using System;
using uPLibrary.Networking.M2Mqtt;
using uPLibrary.Networking.M2Mqtt.Messages;
using System.Text;
using System.Threading.Tasks;

namespace TestLibreriaMqtt
{
    internal class Program
    { 
        static async Task Main(string[] args)
        {
            MqttClient client = new MqttClient("192.168.1.10");
            var state = client.Connect("Client993", "guest", "guest", false, 0, false, null, null, true, 60);
            while (true)
            {
                client.Publish("prova", Encoding.UTF8.GetBytes("Ciao da C#"));
                Console.WriteLine("[SEND]");
                await Task.Delay(10000);
            }
        }
    }
}
