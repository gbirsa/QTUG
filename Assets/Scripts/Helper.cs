using UnityEngine;

[ExecuteAlways]
public class AtmosphereParamsPacker : MonoBehaviour
{
    static readonly int AtmosParams = Shader.PropertyToID("_AtmosParams");

    void OnEnable() => Apply();
    void OnValidate() => Apply();

    void Apply()
    {
        var r = GetComponent<Renderer>();
        if (!r || !r.sharedMaterial) return;

        var m = r.sharedMaterial;
        float sat = m.GetFloat("_Saturation");
        float bri = m.GetFloat("_Brightness");
        float con = m.GetFloat("_Contrast");
        float fog = m.GetFloat("_FogAmount");
        m.SetVector(AtmosParams, new Vector4(sat, bri, con, fog));
    }
}
