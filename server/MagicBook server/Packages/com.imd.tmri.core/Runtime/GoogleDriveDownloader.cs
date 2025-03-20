using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace TMRI.Core
{
    public class GoogleDriveDownloader : MonoBehaviour
    {
        private AssetHandler assetHandler;
        private HashSet<string> inProgressDownloads = new HashSet<string>();
        private const int MaxRetries = 3;
        private const float RetryDelay = 2f;

        public void Initialize(AssetHandler handler)
        {
            assetHandler = handler;
        }

        public void DownloadAssetBundle(AssetPlatformLink platformLink, string name)
        {
            if (inProgressDownloads.Contains(name))
            {
                Debug.LogWarning($"Download for AssetBundle '{name}' is already in progress.");
                return;
            }

            inProgressDownloads.Add(name);
            StartCoroutine(DownloadFileFromGoogleDrive(platformLink, name));
        }

        private IEnumerator DownloadFileFromGoogleDrive(AssetPlatformLink platformLink, string name)
        {
            using (UnityWebRequest request = UnityWebRequest.Get(platformLink.url))
            {
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"Error during initial request: {request.error}\nResponse: {request.downloadHandler.text}");
                    inProgressDownloads.Remove(name);

                    yield return DownloadAndReturnAsset(PlayerPrefs.GetString($"CACHE_{name}_URL"), platformLink.hash, name);

                    yield break;
                }

                string responseText = request.downloadHandler.text;

                if (responseText.Contains("Download anyway"))
                {
                    string formAction = GetFormAction(responseText);
                    string token = GetConfirmationToken(responseText);
                    string uuid = GetUUID(responseText);

                    if (!string.IsNullOrEmpty(token) && !string.IsNullOrEmpty(formAction) && !string.IsNullOrEmpty(uuid))
                    {
                        string downloadUrl = $"{formAction}?confirm={token}&id={GetFileId(platformLink.url)}&export=download&uuid={uuid}";
                        Debug.Log(downloadUrl);
                        yield return StartCoroutine(DownloadAndReturnAsset(downloadUrl, platformLink.hash, name));
                    }
                    else
                    {
                        Debug.LogError("Error: Unable to retrieve confirmation token, form action, or uuid.");
                        inProgressDownloads.Remove(name);
                    }
                }
                else
                {
                    yield return StartCoroutine(DownloadAndReturnAsset(platformLink.url, platformLink.hash, name));
                }
            }
            yield return DownloadAndReturnAsset(platformLink.url, platformLink.hash, name);
        }

        public async Task<byte[]> DownloadFileFromGoogleDrive(string shareLink)
        {
            var dH = new DownloadHandlerBuffer();
            var request = new UnityWebRequest(shareLink)
            {
                downloadHandler = dH
            };

            await request.SendWebRequest();
            
            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"Error during initial request: {request.error}\nResponse: {request.downloadHandler.text}");
                inProgressDownloads.Remove(name);

                return null;
            }

            string responseText = request.downloadHandler.text;

            if (responseText.Contains("Download anyway"))
            {
                string formAction = GetFormAction(responseText);
                string token = GetConfirmationToken(responseText);
                string uuid = GetUUID(responseText);

                if (!string.IsNullOrEmpty(token) && !string.IsNullOrEmpty(formAction) && !string.IsNullOrEmpty(uuid))
                {
                    string downloadUrl = $"{formAction}?confirm={token}&id={GetFileId(shareLink)}&export=download&uuid={uuid}";
                    Debug.Log(downloadUrl);
                    //return downloadUrl;
                    return await DownloadFileFromGoogleDrive(downloadUrl);
                }
                else
                {
                    Debug.LogError("Error: Unable to retrieve confirmation token, form action, or uuid.");
                    inProgressDownloads.Remove(name);
                    return null;
                }
            }
            
            //return request.url;
            return request.downloadHandler.data;
        }

        private IEnumerator DownloadAndReturnAsset(string downloadUrl, string hash, string name)
        {
            //Debug.Log($"{downloadUrl} version {version} and crc {crc} cached: {Caching.IsVersionCached(downloadUrl, Convert.ToInt32(version))}");
            Debug.Log($"Assetbundle '{name}' has the following cached versions:");
            var caches = new List<Hash128>();
            Caching.GetCachedVersions(name, caches);
            foreach(var c in caches)
                Debug.Log($"Hash: {c}");

            for (int attempt = 0; attempt < MaxRetries; attempt++)
            {
                //using (UnityWebRequest downloadRequest = UnityWebRequestAssetBundle.GetAssetBundle(downloadUrl, Hash128.Parse(crc), crc:0))
                using (UnityWebRequest downloadRequest = UnityWebRequestAssetBundle.GetAssetBundle(downloadUrl, new CachedAssetBundle() {
                    hash = Hash128.Parse(hash),
                    name = name
                }, crc:0))
                {
                    yield return downloadRequest.SendWebRequest();

                    if (downloadRequest.result == UnityWebRequest.Result.Success)
                    {
                        PlayerPrefs.SetString($"CACHE_{name}_URL", downloadRequest.url);

                        AssetBundle assetBundle = DownloadHandlerAssetBundle.GetContent(downloadRequest);
                        if (assetBundle != null)
                        {
                            assetHandler.OnAssetBundleDownloaded(name, assetBundle);
                        }
                        else
                        {
                            Debug.LogError("Error: AssetBundle is null.");
                        }

                        inProgressDownloads.Remove(name);
                        yield break;
                    }
                    else
                    {
                        Debug.LogError($"Error during asset bundle download (attempt {attempt + 1}/{MaxRetries}): {downloadRequest.error}");
                    }
                }

                // Wait before retrying
                yield return new WaitForSeconds(RetryDelay);
            }

            // Final cleanup if all retries failed
            Debug.LogError($"Failed to download asset bundle '{name}' after {MaxRetries} attempts.");
            inProgressDownloads.Remove(name);
        }

        private string GetFileId(string url)
        {
            var match = Regex.Match(url, @"id=([^&]+)");
            return match.Success ? match.Groups[1].Value : string.Empty;
        }

        private string GetConfirmationToken(string html)
        {
            return GetValueFromHtml(html, @"<input type=""hidden"" name=""confirm"" value=""([^""]+)"">");
        }

        private string GetFormAction(string html)
        {
            return GetValueFromHtml(html, @"<form[^>]*id=""download-form""[^>]*action=""([^""]+)""");
        }

        private string GetUUID(string html)
        {
            return GetValueFromHtml(html, @"<input type=""hidden"" name=""uuid"" value=""([^""]+)"">");
        }

        private string GetValueFromHtml(string html, string pattern)
        {
            var match = Regex.Match(html, pattern);
            return match.Success ? match.Groups[1].Value : string.Empty;
        }

        public string ConvertDriveDownloadLink(string link)
        {
            if (Regex.Match(link, @"(?:drive\.google\.com\/(?:file\/d\/|open\?id=))([^\/&]+)") is Match m && m.Success && m.Groups.Count >= 2)
            {
                var fileId = m.Groups[1].Value;
                link = $"https://drive.google.com/uc?export=download&id={fileId}";
            }
            return link;
        }
    }
}//namespace TMRI.Client