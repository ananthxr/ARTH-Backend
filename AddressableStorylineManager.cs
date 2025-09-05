using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.Networking;
using TMPro;

/// <summary>
/// Addressable Storyline Manager - Downloads and manages storyline-based asset bundles
/// Handles automatic loading of assets based on volunteer-selected storylines
/// </summary>
public class AddressableStorylineManager : MonoBehaviour
{
    [Header("Storyline Configuration")]
    [Tooltip("Currently loaded storyline (set automatically from web server config)")]
    public string currentStoryline = "";
    
    [Header("Fallback Assets")]
    [Tooltip("Fallback prefabs used when addressable assets fail to load")]
    public GameObject[] fallbackAssets;
    
    [Header("Debug")]
    public bool debugMode = true;
    public TMP_Text mobileDebugText;
    
    [Header("Mobile Network Debugging")]
    [Tooltip("Text field to show network status and detailed errors on mobile")]
    public TMP_Text networkDebugText;
    
    // Asset management
    private Dictionary<int, GameObject> loadedStoryAssets; // Index to loaded asset
    private Dictionary<int, AsyncOperationHandle<GameObject>> loadingOperations; // Track loading operations
    private List<int> failedAssetIndices; // Track failed downloads
    private bool isStorylineLoaded = false;
    private string loadedStorylineName = "";
    
    // Singleton pattern for easy access
    private static AddressableStorylineManager instance;
    public static AddressableStorylineManager Instance => instance;
    
    void Awake()
    {
        LogDebug("=== ADDRESSABLES EARLY DEBUG: AddressableStorylineManager Awake() called ===");
        LogDebug($"Current Storyline: '{currentStoryline}'");
        LogDebug($"Debug Mode: {debugMode}");
        LogDebug($"Fallback Assets Count: {fallbackAssets?.Length ?? 0}");
        LogDebug("=== ADDRESSABLES EARLY DEBUG END ===");
        
        // Singleton setup
        if (instance == null)
        {
            instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
            return;
        }
        
        InitializeCollections();
    }

    void Start()
    {
        LogDebug("=== ADDRESSABLES MAIN DEBUG: AddressableStorylineManager Start() called ===");
        LogDebug("AddressableStorylineManager initialized");
        LogDebug("=== ADDRESSABLES MAIN DEBUG END ===");
    }
    
    private void InitializeCollections()
    {
        loadedStoryAssets = new Dictionary<int, GameObject>();
        loadingOperations = new Dictionary<int, AsyncOperationHandle<GameObject>>();
        failedAssetIndices = new List<int>();
    }
    
    /// <summary>
    /// Test network connectivity and Addressables URL accessibility
    /// </summary>
    private IEnumerator TestNetworkConnectivity()
    {
        LogNetworkDebug("=== NETWORK CONNECTIVITY TEST ===");
        
        // Test basic internet connectivity
        using (UnityWebRequest www = UnityWebRequest.Get("https://www.google.com"))
        {
            www.timeout = 10;
            yield return www.SendWebRequest();
            
            if (www.result == UnityWebRequest.Result.Success)
            {
                LogNetworkDebug("✓ Basic internet connectivity: SUCCESS");
            }
            else
            {
                LogNetworkDebug($"✗ Basic internet connectivity: FAILED - {www.error}");
                yield break;
            }
        }
        
        // Test your ngrok server accessibility
        string testUrl = "https://e47ac2741be4.ngrok-free.app/ServerData/Android/catalog_1.0.0.hash";
        LogNetworkDebug($"Testing Addressables URL: {testUrl}");
        
        using (UnityWebRequest www = UnityWebRequest.Get(testUrl))
        {
            www.timeout = 15;
            yield return www.SendWebRequest();
            
            if (www.result == UnityWebRequest.Result.Success)
            {
                LogNetworkDebug("✓ Addressables URL accessible: SUCCESS");
                LogNetworkDebug($"Response: {www.downloadHandler.text.Substring(0, Mathf.Min(200, www.downloadHandler.text.Length))}...");
            }
            else
            {
                LogNetworkDebug($"✗ Addressables URL failed: {www.error}");
                LogNetworkDebug($"Response Code: {www.responseCode}");
                LogNetworkDebug("This is likely why Addressables are falling back to local assets!");
            }
        }
        
        LogNetworkDebug("=== NETWORK TEST COMPLETE ===");
    }
    
    /// <summary>
    /// Load entire storyline asset bundle
    /// </summary>
    public IEnumerator LoadStorylineAssets(string storylineId)
    {
        if (string.IsNullOrEmpty(storylineId))
        {
            LogError("Storyline ID is null or empty");
            yield break;
        }
        
        LogDebug($"Loading storyline: {storylineId}");
        LogNetworkDebug($"STARTING LOAD: Storyline '{storylineId}'");
        
        // MOBILE DEBUG: Run network connectivity test in parallel (non-blocking)
        StartCoroutine(TestNetworkConnectivity());
        
        currentStoryline = storylineId;
        
        // Clear previous storyline if different
        if (loadedStorylineName != storylineId && isStorylineLoaded)
        {
            yield return StartCoroutine(UnloadCurrentStoryline());
        }
        
        // Load assets using addressable label/group
        yield return StartCoroutine(LoadStorylineAssetGroup(storylineId));
        
        loadedStorylineName = storylineId;
        isStorylineLoaded = true;
        
        LogDebug($"Storyline '{storylineId}' loaded successfully. {loadedStoryAssets.Count} assets available.");
    }
    
    /// <summary>
    /// Load storyline assets by addressable label
    /// </summary>
    private IEnumerator LoadStorylineAssetGroup(string storylineId)
    {
        LogDebug($"Loading addressable assets with label: {storylineId}");
        
        // First check if the label exists
        var checkHandle = Addressables.LoadResourceLocationsAsync(storylineId);
        yield return checkHandle;
        
        if (checkHandle.Status == AsyncOperationStatus.Failed)
        {
            LogError($"CRITICAL: Addressable label '{storylineId}' does not exist!");
            LogError($"Check Unity Addressables Groups window and ensure the label '{storylineId}' is assigned to your asset group");
            LogError($"Exception: {checkHandle.OperationException}");
            Addressables.Release(checkHandle);
            yield break;
        }
        
        var locations = checkHandle.Result;
        LogDebug($"Found {locations.Count} addressable locations for label '{storylineId}'");
        Addressables.Release(checkHandle);
        
        if (locations.Count == 0)
        {
            LogError($"CRITICAL: No assets found with label '{storylineId}'. Check your Addressables configuration!");
            yield break;
        }
        
        // Load all assets with the storyline label
        var loadHandle = Addressables.LoadAssetsAsync<GameObject>(storylineId, null);
        yield return loadHandle;
        
        LogDebug($"=== ADDRESSABLE LOADING RESULTS FOR '{storylineId}' ===");
        LogDebug($"Load Status: {loadHandle.Status}");
        LogDebug($"Operation Exception: {loadHandle.OperationException?.Message ?? "None"}");
        LogDebug($"Handle Valid: {loadHandle.IsValid()}");
        LogDebug($"Percent Complete: {loadHandle.PercentComplete}");
        
        // CRITICAL DEBUG: Log inner exception details
        if (loadHandle.OperationException != null)
        {
            LogError($"DETAILED EXCEPTION INFO:");
            LogError($"Exception Type: {loadHandle.OperationException.GetType().Name}");
            LogError($"Exception Message: {loadHandle.OperationException.Message}");
            LogError($"Stack Trace: {loadHandle.OperationException.StackTrace}");
            if (loadHandle.OperationException.InnerException != null)
            {
                LogError($"Inner Exception: {loadHandle.OperationException.InnerException.Message}");
            }
        }
        
        if (loadHandle.Status == AsyncOperationStatus.Succeeded)
        {
            var loadedAssetsList = loadHandle.Result;
            LogDebug($"SUCCESS: Loaded {loadedAssetsList.Count} assets for storyline '{storylineId}'");
            LogDebug($"Raw asset list from Addressables:");
            for (int i = 0; i < loadedAssetsList.Count; i++)
            {
                LogDebug($"  [{i}] Asset name: '{loadedAssetsList[i].name}', Type: {loadedAssetsList[i].GetType().Name}");
            }
            
            // Map assets to indices based on naming convention
            LogDebug($"=== ASSET INDEX MAPPING PROCESS ===");
            for (int i = 0; i < loadedAssetsList.Count; i++)
            {
                var asset = loadedAssetsList[i];
                int assetIndex = ExtractAssetIndex(asset.name);
                
                if (assetIndex >= 0)
                {
                    loadedStoryAssets[assetIndex] = asset;
                    LogDebug($"SUCCESS: Mapped asset '{asset.name}' to index {assetIndex}");
                }
                else
                {
                    LogWarning($"Could not extract asset index from name: {asset.name} - trying sequential mapping");
                    // Fallback: use sequential index if name parsing fails
                    loadedStoryAssets[i] = asset;
                    LogDebug($"FALLBACK: Mapped asset '{asset.name}' to sequential index {i}");
                }
            }
            
            LogDebug($"=== FINAL MAPPING RESULTS ===");
            LogDebug($"Total mapped assets: {loadedStoryAssets.Count}");
            foreach (var kvp in loadedStoryAssets)
            {
                LogDebug($"  Index {kvp.Key}: '{kvp.Value.name}'");
            }
        }
        else
        {
            LogError($"=== CRITICAL: ADDRESSABLE LOADING FAILED ===");
            LogError($"Storyline label: '{storylineId}'");
            LogError($"Status: {loadHandle.Status}");
            LogError($"Exception: {loadHandle.OperationException}");
            LogError($"This means your Addressables are not properly built or the remote URL is unreachable!");
            LogError($"Check: 1) Addressables built with correct labels, 2) Remote URL accessible, 3) Network connectivity");
        }
        
        // Keep reference to handle for cleanup
        // Note: Don't release handle immediately as we need the assets
    }
    
    /// <summary>
    /// Extract asset index from asset name (e.g., "PirateTreasure_Clue0" -> 0)
    /// </summary>
    private int ExtractAssetIndex(string assetName)
    {
        LogDebug($"Extracting asset index from: '{assetName}'");
        
        try
        {
            // Look for patterns like "_Clue0", "_0", "_Asset0"
            string[] patterns = { "_Clue", "_Asset", "_" };
            
            foreach (string pattern in patterns)
            {
                int patternIndex = assetName.LastIndexOf(pattern);
                if (patternIndex >= 0)
                {
                    string numberPart = assetName.Substring(patternIndex + pattern.Length);
                    LogDebug($"Found pattern '{pattern}' in '{assetName}', number part: '{numberPart}'");
                    if (int.TryParse(numberPart, out int index))
                    {
                        LogDebug($"Successfully extracted index {index} from '{assetName}'");
                        return index;
                    }
                }
            }
            
            // If no pattern found, try to parse the last character
            string lastChar = assetName.Substring(assetName.Length - 1);
            LogDebug($"No pattern found, trying last character: '{lastChar}'");
            if (int.TryParse(lastChar, out int lastIndex))
            {
                LogDebug($"Successfully extracted last character index {lastIndex} from '{assetName}'");
                return lastIndex;
            }
        }
        catch (System.Exception e)
        {
            LogWarning($"Exception extracting asset index from '{assetName}': {e.Message}");
        }
        
        LogWarning($"Could not extract asset index from '{assetName}'");
        return -1; // Index not found
    }
    
    /// <summary>
    /// Get asset by storyline index with fallback support
    /// </summary>
    public GameObject GetStoryAsset(int assetIndex)
    {
        LogDebug($"=== DETAILED ASSET REQUEST ANALYSIS FOR INDEX {assetIndex} ===");
        LogDebug($"Storyline loaded: {isStorylineLoaded}");
        LogDebug($"Current storyline name: '{currentStoryline}'");
        LogDebug($"Loaded storyline name: '{loadedStorylineName}'");
        LogDebug($"Total loaded assets count: {loadedStoryAssets.Count}");
        LogDebug($"Asset failed previously: {failedAssetIndices.Contains(assetIndex)}");
        LogDebug($"Available asset indices: [{string.Join(", ", loadedStoryAssets.Keys)}]");
        
        // Check if we have the loaded asset
        if (loadedStoryAssets.ContainsKey(assetIndex))
        {
            LogDebug($"SUCCESS! Using storyline asset for index {assetIndex}: {loadedStoryAssets[assetIndex].name}");
            LogDebug($"=== ASSET REQUEST ANALYSIS END - SUCCESS ===");
            return loadedStoryAssets[assetIndex];
        }
        
        // Check if asset failed to load previously
        if (failedAssetIndices.Contains(assetIndex))
        {
            LogWarning($"FALLBACK REASON: Asset {assetIndex} previously failed to load");
            LogDebug($"=== ASSET REQUEST ANALYSIS END - PREVIOUS FAILURE ===");
            return GetFallbackAsset(assetIndex);
        }
        
        // Debug why asset is not available
        if (!isStorylineLoaded)
        {
            LogError($"FALLBACK REASON: Storyline '{currentStoryline}' is NOT LOADED yet!");
            LogError($"This means LoadStorylineAssets() either failed or hasn't completed!");
        }
        else if (loadedStoryAssets.Count == 0)
        {
            LogError($"FALLBACK REASON: Storyline '{loadedStorylineName}' loaded but contains ZERO assets!");
            LogError($"This means the Addressable loading succeeded but returned empty results!");
        }
        else
        {
            LogError($"FALLBACK REASON: Asset index {assetIndex} not found in loaded assets!");
            LogError($"Loaded storyline '{loadedStorylineName}' contains {loadedStoryAssets.Count} assets with indices: [{string.Join(", ", loadedStoryAssets.Keys)}]");
            LogError($"This means asset naming/indexing doesn't match your config storyAssetIndex values!");
        }
        
        LogDebug($"=== ASSET REQUEST ANALYSIS END - USING FALLBACK ===");
        return GetFallbackAsset(assetIndex);
    }
    
    /// <summary>
    /// Get fallback asset for given index
    /// </summary>
    private GameObject GetFallbackAsset(int assetIndex)
    {
        if (fallbackAssets != null && assetIndex >= 0 && assetIndex < fallbackAssets.Length)
        {
            if (fallbackAssets[assetIndex] != null)
            {
                LogDebug($"Using fallback asset for index {assetIndex}: {fallbackAssets[assetIndex].name}");
                return fallbackAssets[assetIndex];
            }
        }
        
        // Last resort: return first available fallback or null
        if (fallbackAssets != null && fallbackAssets.Length > 0 && fallbackAssets[0] != null)
        {
            LogWarning($"Using first fallback asset for index {assetIndex}");
            return fallbackAssets[0];
        }
        
        LogError($"No fallback asset available for index {assetIndex}");
        return null;
    }
    
    /// <summary>
    /// Check if storyline is loaded and ready
    /// </summary>
    public bool IsStorylineLoaded(string storylineId = null)
    {
        if (!string.IsNullOrEmpty(storylineId))
        {
            return isStorylineLoaded && loadedStorylineName == storylineId;
        }
        return isStorylineLoaded;
    }
    
    /// <summary>
    /// Check if specific asset is loaded
    /// </summary>
    public bool IsAssetLoaded(int assetIndex)
    {
        return loadedStoryAssets.ContainsKey(assetIndex);
    }
    
    /// <summary>
    /// Get currently loaded storyline info
    /// </summary>
    public string GetCurrentStoryline()
    {
        return loadedStorylineName;
    }
    
    /// <summary>
    /// Get number of loaded assets
    /// </summary>
    public int GetLoadedAssetCount()
    {
        return loadedStoryAssets.Count;
    }
    
    /// <summary>
    /// Unload current storyline assets
    /// </summary>
    private IEnumerator UnloadCurrentStoryline()
    {
        LogDebug($"Unloading previous storyline: {loadedStorylineName}");
        
        // Release any loading operations
        foreach (var kvp in loadingOperations)
        {
            if (kvp.Value.IsValid())
            {
                Addressables.Release(kvp.Value);
            }
        }
        loadingOperations.Clear();
        
        // Clear loaded assets (Addressables will handle cleanup)
        loadedStoryAssets.Clear();
        failedAssetIndices.Clear();
        
        isStorylineLoaded = false;
        loadedStorylineName = "";
        
        yield return null; // Allow one frame for cleanup
    }
    
    /// <summary>
    /// Preload storyline based on config (called from WebServerImageManager)
    /// </summary>
    public void PreloadStorylineFromConfig(StorylineConfig storylineConfig)
    {
        if (storylineConfig != null && !string.IsNullOrEmpty(storylineConfig.selectedStory))
        {
            if (!IsStorylineLoaded(storylineConfig.selectedStory))
            {
                LogDebug($"Preloading storyline: {storylineConfig.selectedStory} - {storylineConfig.storyTitle}");
                StartCoroutine(LoadStorylineAssets(storylineConfig.selectedStory));
            }
            else
            {
                LogDebug($"Storyline '{storylineConfig.selectedStory}' already loaded");
            }
        }
        else
        {
            LogWarning("No storyline config provided for preloading");
        }
    }
    
    private void LogDebug(string message)
    {
        if (debugMode)
        {
            Debug.Log($"[AddressableStorylineManager] {message}");
            if (mobileDebugText != null)
            {
                mobileDebugText.text = message;
            }
        }
    }
    
    private void LogNetworkDebug(string message)
    {
        if (debugMode)
        {
            string fullMessage = $"[AddressableStorylineManager-NETWORK] {message}";
            Debug.Log(fullMessage);
            
            // Mobile network debugging - show on both debug text fields
            if (mobileDebugText != null)
            {
                mobileDebugText.text = fullMessage;
            }
            
            if (networkDebugText != null)
            {
                networkDebugText.text = fullMessage;
            }
        }
    }
    
    private void LogWarning(string message)
    {
        Debug.LogWarning($"[AddressableStorylineManager] {message}");
        if (mobileDebugText != null)
        {
            mobileDebugText.text = $"WARNING: {message}";
        }
    }
    
    private void LogError(string message)
    {
        Debug.LogError($"[AddressableStorylineManager] {message}");
        if (mobileDebugText != null)
        {
            mobileDebugText.text = $"ERROR: {message}";
        }
    }
    
    void OnDestroy()
    {
        // Cleanup on destroy
        if (loadingOperations != null)
        {
            foreach (var kvp in loadingOperations)
            {
                if (kvp.Value.IsValid())
                {
                    Addressables.Release(kvp.Value);
                }
            }
        }
    }
}