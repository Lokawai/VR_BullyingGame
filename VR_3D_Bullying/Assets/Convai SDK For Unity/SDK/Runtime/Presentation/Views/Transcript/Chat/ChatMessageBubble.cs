using Convai.Runtime.Behaviors;
using Convai.Runtime.Services.CharacterLocator;
using TMPro;
using UnityEngine;

namespace Convai.Runtime.Presentation.Views.Transcript
{
    public class ChatMessageBubble : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI senderUI;
        [SerializeField] private TextMeshProUGUI messageUI;
        private IConvaiCharacterLocatorService _characterLocatorService;
        private string _interactionID;
        private string _message;
        public string Identifier { get; set; }
        public bool IsCompleted { get; set; } = false;
        public void SetSender(string sender) => senderUI.text = sender;
        public void SetSenderColor(Color nameTagColor) => senderUI.color = nameTagColor;

        public void SetMessage(string message)
        {
            messageUI.text = message;
            _message = message;
        }

        public void AppendMessage(string message) => SetMessage(messageUI.text + message);
        public void SetInteractionID(string interactionID) => _interactionID = interactionID;

        public void SetLocatorService(IConvaiCharacterLocatorService locatorService) =>
            _characterLocatorService = locatorService;

        public bool SendFeedback(bool isPositiveFeedback)
        {
            if (string.IsNullOrEmpty(_interactionID)) return false;

            if (_characterLocatorService == null ||
                !_characterLocatorService.TryGetCharacter(Identifier, out IConvaiCharacterAgent character))
                return false;

            return true;
        }

        public void SetSenderUIActive(bool isActive) => senderUI.gameObject.SetActive(isActive);
        public void SetMessageUIActive(bool isActive) => messageUI.gameObject.SetActive(isActive);
    }
}
