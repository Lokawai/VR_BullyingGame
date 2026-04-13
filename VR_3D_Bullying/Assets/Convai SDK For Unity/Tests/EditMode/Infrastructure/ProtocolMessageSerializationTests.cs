using System.Collections.Generic;
using Convai.Infrastructure.Protocol.Messages;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Convai.Tests.EditMode.Infrastructure
{
    [TestFixture]
    public class ProtocolMessageSerializationTests
    {
        [Test]
        public void Serialize_RTVITriggerMessage_ContainsExpectedKeys()
        {
            var message = new RTVITriggerMessage("wake_up", "hello");
            JObject obj = JObject.Parse(JsonConvert.SerializeObject(message));

            Assert.AreEqual("trigger-message", obj["type"]?.ToString());
            Assert.AreEqual("rtvi-ai", obj["label"]?.ToString());
            Assert.IsFalse(string.IsNullOrEmpty(obj["id"]?.ToString()));
            Assert.AreEqual("wake_up", obj["data"]?["trigger_name"]?.ToString());
            Assert.AreEqual("hello", obj["data"]?["trigger_message"]?.ToString());
        }

        [Test]
        public void Serialize_RTVIUpdateTemplateKeys_ContainsExpectedKeys()
        {
            var message = new RTVIUpdateTemplateKeys(
                new Dictionary<string, string> { { "foo", "bar" } });
            JObject obj = JObject.Parse(JsonConvert.SerializeObject(message));

            Assert.AreEqual("update-template-keys", obj["type"]?.ToString());
            Assert.AreEqual("bar", obj["data"]?["template_keys"]?["foo"]?.ToString());
        }

        [Test]
        public void Serialize_RTVIUpdateSceneMetadata_ContainsExpectedKeys()
        {
            var message = new RTVIUpdateSceneMetadata(
                new List<SceneMetadata> { new() { Name = "Town", Description = "Center square" } });
            JObject obj = JObject.Parse(JsonConvert.SerializeObject(message));

            Assert.AreEqual("update-scene-metadata", obj["type"]?.ToString());
            Assert.AreEqual("Town", obj["data"]?[0]?["name"]?.ToString());
            Assert.AreEqual("Center square", obj["data"]?[0]?["description"]?.ToString());
        }

        [Test]
        public void Serialize_AnyOutboundMessage_ContainsEnvelopeKeys()
        {
            var message = new RTVIUserTextMessage("hello");
            JObject obj = JObject.Parse(JsonConvert.SerializeObject(message));

            Assert.AreEqual("rtvi-ai", obj["label"]?.ToString());
            Assert.AreEqual("user_text_message", obj["type"]?.ToString());
            Assert.IsFalse(string.IsNullOrEmpty(obj["id"]?.ToString()));
            Assert.NotNull(obj["data"]);
        }
    }
}
