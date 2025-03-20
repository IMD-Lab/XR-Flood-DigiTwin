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

                //string responseText = request.downloadHandler.text;

                byte[] downloadAnywayBytes = new byte[] { 68, 111, 119, 110, 108, 111, 97, 100, 32, 97, 110, 121, 119, 97, 121 };
                if (request.downloadHandler.data.AsSpan().IndexOf(downloadAnywayBytes) >= 0)
                {
                    string formAction = GetFormAction(request.downloadHandler.data);
                    string token = GetConfirmationToken(request.downloadHandler.data);
                    string uuid = GetUUID(request.downloadHandler.data);

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

            byte[] downloadAnywayBytes = new byte[] { 68, 111, 119, 110, 108, 111, 97, 100, 32, 97, 110, 121, 119, 97, 121 }; //"Download anyway"
            //string responseText = request.downloadHandler.text;

            if (request.downloadHandler.data.AsSpan().IndexOf(downloadAnywayBytes) >= 0) // If data contains "Download anyway"
            {
                string formAction = GetFormAction(request.downloadHandler.data);
                string token = GetConfirmationToken(request.downloadHandler.data);
                string uuid = GetUUID(request.downloadHandler.data);

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

        //<input type = "hidden" name="confirm" value="t">
        private string GetConfirmationToken(byte[] html)
        {
            byte[] Confirmation_pattern = new byte[]{
                    60, 105, 110, 112, 117, 116, 32, 116, 121, 112, 101, 61, 34, 104, 105, 100, 100, 101, 110, 34,
                    32, 110, 97, 109, 101, 61, 34, 99, 111, 110, 102, 105, 114, 109, 34, 32, 118, 97, 108, 117, 101, 61, 34
                };// `<input type="hidden" name="confirm" value="`

            byte[] endPattern1 = new byte[] { 34, 62 }; // `">`
            return GetValueFromHtmlBytes(html, Confirmation_pattern, endPattern1);
            //return confirmmation token
        }

        //<form id="download-form" action="https://drive.usercontent.google.com/download" method="get">
        private string GetFormAction(byte[] html)
        {
            byte[] FormAction_pattern = new byte[]
            {
                60, 102, 111, 114, 109, 32, 105, 100, 61, 34, 100, 111, 119, 110, 108, 111, 97, 100, 45, 102, 111, 114, 109, 34,
                32, 97, 99, 116, 105, 111, 110, 61, 34 // `<form id="download-form" action="`
            };
            byte[] endPattern = new byte[] { 34}; // Find the next `"` before `method="get"`
            return GetValueFromHtmlBytes(html, FormAction_pattern, endPattern);
            //return Google Drive Download “https://drive.usercontent.google.com/download”
        }

        //<input type = "hidden" name="uuid" value="ba35037a-bfe4-443a-b9a8-f8bc068dc4f9">
        private string GetUUID(byte[] html)
        {
            byte[] UUIDpattern = new byte[]
            {
                60, 105, 110, 112, 117, 116, 32, 116, 121, 112, 101, 61, 34, 104, 105, 100, 100, 101, 110, 34,
                32, 110, 97, 109, 101, 61, 34, 117, 117, 105, 100, 34, 32, 118, 97, 108, 117, 101, 61, 34 // `<input type="hidden" name="uuid" value="`
            };
            byte[] endPattern = new byte[] { 34, 62 };// ` >" `
            return GetValueFromHtmlBytes(html, UUIDpattern, endPattern);
            //return UUID “ba35037a-bfe4-443a-b9a8-f8bc068dc4f9”
        }

        private string GetValueFromHtmlBytes(byte[] htmlBytes, byte[] pattern, byte[] endPattern)
        {
            int startIndex = IndexOfPatternBytes(htmlBytes, pattern);
            if (startIndex == -1) return string.Empty;
            startIndex += pattern.Length;

            int endIndex = IndexOfPatternBytes(htmlBytes, endPattern, startIndex);
            if (endIndex == -1) return string.Empty;

            byte[] valueBytes = new byte[endIndex - startIndex];
            Array.Copy(htmlBytes, startIndex, valueBytes, 0, endIndex - startIndex);

            return BytesToAsciiString(valueBytes);
        }

        private int IndexOfPatternBytes(byte[] data, byte[] pattern, int start = 0)
        {
            for (int i = start; i <= data.Length - pattern.Length; i++)
            {
                bool found = true;
                for (int j = 0; j < pattern.Length; j++)
                {
                    if (data[i + j] != pattern[j])
                    {
                        found = false;
                        break;
                    }
                }
                if (found) return i;
            }
            return -1;
        }

        private string BytesToAsciiString(byte[] data)
        {   //ASCII Decoding the raw byte data to String 
            char[] chars = new char[data.Length];
            for (int i = 0; i < data.Length; i++)
            {
                chars[i] = (char)data[i];
            }
            return new string(chars);
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