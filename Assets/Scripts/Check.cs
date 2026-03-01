using UnityEngine;

public class LayerRendererDump : MonoBehaviour
{
    public string unityLayerName = "Foreground";

    void Start()
    {
        int layer = LayerMask.NameToLayer(unityLayerName);
        Debug.Log($"Layer '{unityLayerName}' index = {layer}");

        var rends = FindObjectsOfType<Renderer>(true);
        int count = 0;
        foreach (var r in rends)
        {
            if (r.gameObject.layer == layer && r.enabled && r.gameObject.activeInHierarchy)
            {
                count++;
                Debug.Log($"FG Renderer: {r.name}  (layer={LayerMask.LayerToName(r.gameObject.layer)})");
            }
        }
        Debug.Log($"Total renderers on '{unityLayerName}': {count}");
    }
}
