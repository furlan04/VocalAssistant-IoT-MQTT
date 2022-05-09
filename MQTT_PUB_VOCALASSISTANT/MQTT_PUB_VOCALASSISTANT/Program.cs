using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Intent;
using HttpProxyControl;
using AliceNeural.Model;
using System.Linq;
using System.Text;
using uPLibrary.Networking.M2Mqtt;
using uPLibrary.Networking.M2Mqtt.Messages;

namespace AliceNeural
{
    #region JSON CLASSI
    public class AzureSpeechServiceStore
    {
        [JsonPropertyName("api_key")]
        public string APIKeyValue { get; set; }
        [JsonPropertyName("region_location")]
        public string RegionLocation { get; set; }
        [JsonPropertyName("endpoint")]
        public string EndPoint { get; set; }
    }
    public class Motore
    {
        public string direzione { get; set; }
        public int velocità { get; set; }
    }
    public class ValoreSensore
    {
        [JsonPropertyName("valore-sensore")]
        public float val { get; set; }
    }
    public class AzureIntentRecognitionStore
    {
        [JsonPropertyName("luis_app_name")]
        public string LuisAppName { get; set; }
        [JsonPropertyName("luis_app_id")]
        public string LuiAppId { get; set; }
        [JsonPropertyName("luis_culture")]
        public string LuisCulture { get; set; }
        [JsonPropertyName("azure_resource")]
        public AzureResource AzureResource { get; set; }
    }
    public class AzureResource
    {
        [JsonPropertyName("resource_name")]
        public string ResourceName { get; set; }
        [JsonPropertyName("api_key")]
        public string ApiKey { get; set; }
        [JsonPropertyName("region_location")]
        public string RegionLocation { get; set; }
        [JsonPropertyName("endpoint")]
        public string Endpoint { get; set; }
    }
    #endregion
    class Program
    {
        #region CHIAVI e VARIABILI
        static AzureSpeechServiceStore azureSpeechServiceStore = GetSpeechDataFromStore("../../../voice-keys.json");
        static readonly string azureSpeechServiceKey = azureSpeechServiceStore.APIKeyValue;
        static readonly string azureSpeechServiceRegion = azureSpeechServiceStore.RegionLocation;
        static AzureIntentRecognitionStore azureIntentRecognitionStore = GetIntentDataFromStore();
        static readonly string azureIntentServiceKey = azureIntentRecognitionStore.AzureResource.ApiKey;
        static readonly string azureIntentServiceRegion = azureIntentRecognitionStore.AzureResource.RegionLocation;
        static readonly string luisIntentServiceAppId = azureIntentRecognitionStore.LuiAppId;
        static readonly string luisIntentServiceCulture = azureIntentRecognitionStore.LuisCulture;
        static MqttClient mqttClient_motore = new MqttClient("192.168.1.8");
        static MqttClient mqttClient_sensore_temp = new MqttClient("192.168.1.8");
        static string ultimoValoreSensoreTemp = "Nessun Dato";
        static string ultimoValoreSensoreHum = "Nessun Dato";
        public static AzureSpeechServiceStore GetSpeechDataFromStore(string keyStorePath)
        {
            string store = File.ReadAllText(keyStorePath);
            AzureSpeechServiceStore azureSpeechServiceStore = JsonSerializer.Deserialize<AzureSpeechServiceStore>(store);
            return azureSpeechServiceStore;
        }
        static AzureIntentRecognitionStore GetIntentDataFromStore()
        {
            string keyStorePath = "../../../intent-keys.json";
            string store = File.ReadAllText(keyStorePath);
            AzureIntentRecognitionStore azureSpeechServiceStore = JsonSerializer.Deserialize<AzureIntentRecognitionStore>(store);
            return azureSpeechServiceStore;
        }
        #endregion
        static void Main(string[] args)
        {
            var config = SpeechConfig.FromSubscription(azureSpeechServiceKey, azureSpeechServiceRegion);
            ProxyParams? proxyParams = HttpProxyHelper.GetHttpClientProxyParams();
            if (proxyParams.HasValue)
            {
                config.SetProxy(proxyParams.Value.ProxyAddress.Split('/').Last(), proxyParams.Value.ProxyPort);
            }
            config.SpeechRecognitionLanguage = "it-IT";
            config.SpeechSynthesisLanguage = "it-IT";
            config.SpeechSynthesisVoiceName = "it-IT-DiegoNeural";
            var state = mqttClient_motore.Connect("Client993", "guest", "guest", false, 0, false, null, null, true, 60);
            mqttClient_sensore_temp.MqttMsgPublishReceived += (sender, e) =>
            {
                Console.WriteLine(System.Text.Encoding.UTF8.GetString(e.Message, 0, e.Message.Length));
                if(e.Topic == "tps/sensore/temperatura")
                    ultimoValoreSensoreTemp = JsonSerializer.Deserialize<ValoreSensore>(System.Text.Encoding.UTF8.GetString(e.Message, 0, e.Message.Length)).val.ToString();
                else
                    ultimoValoreSensoreHum = JsonSerializer.Deserialize<ValoreSensore>(System.Text.Encoding.UTF8.GetString(e.Message, 0, e.Message.Length)).val.ToString();
            };
            mqttClient_sensore_temp.Subscribe(new string[] { "tps/sensore/temperatura" }, new byte[] { MqttMsgBase.QOS_LEVEL_EXACTLY_ONCE });
            mqttClient_sensore_temp.Subscribe(new string[] { "tps/sensore/umidità" }, new byte[] { MqttMsgBase.QOS_LEVEL_EXACTLY_ONCE });
            mqttClient_sensore_temp.Connect("AssistenteVocale");
            IntentPatternMatchingWithMicrophoneAsync(config).Wait();
        }
        private static async Task IntentPatternMatchingWithMicrophoneAsync(SpeechConfig config)
        {
            using (var intentRecognizer = new IntentRecognizer(config))
            {
                const string fraseOK = "ok";
                const string fraseStop = "stop";
                intentRecognizer.AddIntent(fraseOK, "Ok");
                intentRecognizer.AddIntent(fraseStop, "Stop");
                var stopRecognition = new TaskCompletionSource<int>();
                using var synthesizer = new SpeechSynthesizer(config);
                SpeechConfig speechConfigForLuis = SpeechConfig.FromSubscription(azureIntentServiceKey, azureIntentServiceRegion);
                speechConfigForLuis.SpeechRecognitionLanguage = luisIntentServiceCulture;
                ProxyParams? proxyParams = HttpProxyHelper.GetHttpClientProxyParams();
                if (proxyParams.HasValue)
                {
                    speechConfigForLuis.SetProxy(proxyParams.Value.ProxyAddress.Split('/').Last(), proxyParams.Value.ProxyPort);
                }
                LanguageUnderstandingModel model = LanguageUnderstandingModel.FromAppId(luisIntentServiceAppId);
                intentRecognizer.Recognized += async (s, e) =>
                {
                    switch (e.Result.Reason)
                    {
                        case ResultReason.RecognizedSpeech:
                            Console.WriteLine($"PATTERN MATCHING - RECOGNIZED: Text= { e.Result.Text}");
                            if (ContainsTokens(e.Result.Text, fraseOK))
                            {
                                await HandleOkCommand(synthesizer, speechConfigForLuis, model);
                            }
                            else if (ContainsTokens(e.Result.Text, fraseStop))
                            {
                                Console.WriteLine("Stopping current speaking...");
                                await synthesizer.StopSpeakingAsync();
                            }
                            break;
                        case ResultReason.RecognizedIntent:
                            {
                                Console.WriteLine($"PATTERN MATCHING - RECOGNIZED: Text= {e.Result.Text}");
                                Console.WriteLine($" Intent Id= {e.Result.IntentId}.");
                                if (e.Result.IntentId == "Ok")
                                {
                                    await HandleOkCommand(synthesizer, speechConfigForLuis, model);
                                }
                                else if (e.Result.IntentId == "Stop")
                                {
                                    Console.WriteLine("Stopping current speaking...");
                                    await synthesizer.StopSpeakingAsync();
                                }
                            }
                            break;
                        case ResultReason.NoMatch:
                            Console.WriteLine($"NOMATCH: Speech could not be recognized.");
                            var noMatch = NoMatchDetails.FromResult(e.Result);
                            switch (noMatch.Reason)
                            {
                                case NoMatchReason.NotRecognized:
                                    Console.WriteLine($"PATTERN MATCHING - NOMATCH: Speech was detected, but not recognized.");
                                    break;
                                case NoMatchReason.InitialSilenceTimeout:
                                    Console.WriteLine($"PATTERN MATCHING - NOMATCH: The start of the audio stream contains only silence, and the service timed out waiting for speech.");
                                    break;
                                case NoMatchReason.InitialBabbleTimeout:
                                    Console.WriteLine($"PATTERN MATCHING - NOMATCH: The start of the audio stream contains only noise, and the service timed out waiting for speech.");
                                    break;
                                case NoMatchReason.KeywordNotRecognized:
                                    Console.WriteLine($"PATTERN MATCHING - NOMATCH: Keyword not recognized");
                                    break;
                            }
                            break;
                        default:
                            break;
                    }
                };
                intentRecognizer.Canceled += (s, e) =>
                {
                    Console.WriteLine($"PATTERN MATCHING - CANCELED: Reason={e.Reason}");
                    if (e.Reason == CancellationReason.Error)
                    {
                        Console.WriteLine($"PATTERN MATCHING - CANCELED: ErrorCode ={ e.ErrorCode}");
                        Console.WriteLine($"PATTERN MATCHING - CANCELED: ErrorDetails ={ e.ErrorDetails}");
                        Console.WriteLine($"PATTERN MATCHING - CANCELED: Did you update the speech key and location / region info ? ");
                    }
                    stopRecognition.TrySetResult(0);
                };
                intentRecognizer.SessionStopped += (s, e) =>
                {
                    Console.WriteLine("\n Session stopped event.");
                    stopRecognition.TrySetResult(0);
                };
                Console.WriteLine($"In ascolto. Dì \"{fraseOK}\" per impartire un ordine, oppure \"{fraseStop}\" per fermare l'azione in corso");
                await intentRecognizer.StartContinuousRecognitionAsync();
                Task.WaitAny(new[] { stopRecognition.Task });
            }
        }
        private static async Task HandleOkCommand(SpeechSynthesizer synthesizer, SpeechConfig speechConfigForLuis, LanguageUnderstandingModel model)
        {
            await synthesizer.SpeakTextAsync("Sono in ascolto...");
            string jsonResult = await RecognizeIntentAsync(speechConfigForLuis, model);
            LanguageUnderstandingServiceResponse languageUnderstandingModel = null;
            if (jsonResult != null)
                languageUnderstandingModel = JsonSerializer.Deserialize<LanguageUnderstandingServiceResponse>(jsonResult);
            if (languageUnderstandingModel != null && languageUnderstandingModel.topScoringIntent.score > 0.25)
            {
                string intent = languageUnderstandingModel.topScoringIntent.intent;
                if(intent.Equals("IoT.Motore"))
                {
                    int vel = 128;
                    string dir = "destra";
                    foreach (var item in languageUnderstandingModel.entities)
                    {
                        if (item.type.Equals("IoT.Direzione"))
                        {
                            dir = item.entity;
                        }
                        else if(item.type.Equals("IoT.Velocità"))
                        {
                            vel = int.Parse(item.entity);
                        }
                    }
                    if (dir.Contains("spegn") || dir.Contains("dis"))
                        vel = 0;
                    string motoreJson = JsonSerializer.Serialize<Motore>(new Motore { direzione = dir, velocità = vel});
                    await Task.Run(() =>
                    {
                        if (mqttClient_motore.IsConnected)
                        {
                            mqttClient_motore.Publish("tps/motore", Encoding.UTF8.GetBytes(motoreJson));
                        }
                    });
                    await synthesizer.SpeakTextAsync("Messaggio inviato su mqtt");
                }
                else if(intent.Equals("IoT.Sensore"))
                {
                    string condition = "none";
                    foreach (var item in languageUnderstandingModel.entities)
                    {
                        if (item.type.Equals("Weather.WeatherCondition"))
                        {
                            condition = item.entity;
                        }
                    }
                    switch (condition)
                    {
                        case "temperatura":
                            Console.WriteLine($"L'ultimo dato sulla temperatura è {ultimoValoreSensoreTemp}");
                            await synthesizer.SpeakTextAsync($"L'ultimo dato sulla temperatura è {ultimoValoreSensoreTemp}");
                            break;
                        case "umidità":
                            Console.WriteLine($"L'ultimo dato sulla umidità è {ultimoValoreSensoreHum}");
                            await synthesizer.SpeakTextAsync($"L'ultimo dato sulla umidità è {ultimoValoreSensoreHum}");
                            break;
                        default:
                            await synthesizer.SpeakTextAsync($"L'ultimo dato sulla temperatura è {ultimoValoreSensoreTemp} e quello sull'umidità è {ultimoValoreSensoreHum}");
                            break;
                    }
                }
            }
            else
            {
                await synthesizer.SpeakTextAsync("Non ho capito.");
            }
        }
        public static async Task<string> RecognizeIntentAsync(SpeechConfig config, LanguageUnderstandingModel model)
        {
            string jsonResult = null;
            using (var recognizer = new IntentRecognizer(config))
            {
                recognizer.AddAllIntents(model);
                Console.WriteLine("Say something...");
                var result = await recognizer.RecognizeOnceAsync();
                switch (result.Reason)
                {
                    case ResultReason.RecognizedIntent:
                        Console.WriteLine($"RECOGNIZED: Text={result.Text}");
                        Console.WriteLine($" Intent Id: {result.IntentId}.");
                        jsonResult = result.Properties.GetProperty(PropertyId.LanguageUnderstandingServiceResponse_JsonResult);
                        Console.WriteLine($"{jsonResult}.");
                        break;
                    case ResultReason.RecognizedSpeech:
                        Console.WriteLine($"RECOGNIZED: Text={result.Text}");
                        Console.WriteLine($" Intent not recognized.");
                        break;
                    case ResultReason.NoMatch:
                        Console.WriteLine($"NOMATCH: Speech could not be recognized.");
                        break;
                    case ResultReason.Canceled:
                        var cancellation = CancellationDetails.FromResult(result);
                        Console.WriteLine($"CANCELED: Reason={cancellation.Reason}");
                        if (cancellation.Reason == CancellationReason.Error)
                        {
                            Console.WriteLine($"CANCELED: ErrorCode ={ cancellation.ErrorCode}");
                            Console.WriteLine($"CANCELED: ErrorDetails ={ cancellation.ErrorDetails}");
                            Console.WriteLine($"CANCELED: Did you update the subscription info ? ");
                        }
                        break;
                }
                return jsonResult;
            }
        }
        public static bool ContainsTokens(string phrase, string stringWithTokens)
        {
            char[] separators = { ',', ' ', ';', '.', '?', '!' };
            string[] tokens = stringWithTokens.Split(separators,
           StringSplitOptions.RemoveEmptyEntries);
            Console.WriteLine($"ContainsTokens-> phrase: {phrase}");
            Console.WriteLine("ContainsTokens -> tokens: ");
            foreach (var item in tokens)
            {
                Console.Write(item + " ");
            }
            Console.WriteLine();
            foreach (var token in tokens)
            {
                if (!phrase.Contains(token, StringComparison.CurrentCultureIgnoreCase))
                {
                    return false;
                }
            }
            return true;
        }
    }
}
