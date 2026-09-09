using SteamKit2.WebUI.Internal;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Security.Credentials;

namespace ControllerPlayground.Services.Steam.SteamKit {
    public sealed class SteamCredentialStore {
        private const string ResourceName = "ControllerPlayground.Steam";
        private readonly PasswordVault _vault = new();
        public void Save(string accountName, string refreshToken) {
            Clear();

            PasswordCredential credential = new(
                ResourceName, 
                accountName, 
                refreshToken);

            _vault.Add(credential);
        }

        public PasswordCredential? Load() {
            try { 
                var credentials = _vault.FindAllByResource(ResourceName);

                if (credentials.Count == 0)
                    return null;

                PasswordCredential credential = credentials[0];

                credential.RetrievePassword();

                return credential;

            } catch {
                return null;
            }
        }

        public void Clear() {
            try { 
                var credentials = _vault.FindAllByResource(ResourceName);
                foreach (var credential in credentials) {
                    _vault.Remove(credential);
                }
            } catch {
                // No saved Steam credentials found, nothing to clear
            }
        }
    }
}
