using UnityEngine;
using UnityEngine.Events;
using Fusion;

namespace Stage1
{
    public class PasswordKeypad : NetworkBehaviour
    {
        public string correctPassword = "1234"; // Date + Bottles
        private string currentInput = "";
        
        public UnityEvent OnSuccess;
        public UnityEvent OnFailure;

        public void InputDigit(string digit)
        {
            currentInput += digit;
            if (currentInput.Length == correctPassword.Length)
            {
                if (currentInput == correctPassword)
                {
                    OnSuccess?.Invoke();
                }
                else
                {
                    OnFailure?.Invoke();
                    currentInput = ""; // Reset
                }
            }
        }
    }
}
