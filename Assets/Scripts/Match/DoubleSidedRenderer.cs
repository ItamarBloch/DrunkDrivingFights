using UnityEngine;

/// <summary>
/// Drop this on any GameObject (e.g. the ramp) to make all of its renderers
/// draw both front and back faces. Fixes the "invisible from below" problem
/// caused by Unity's default backface culling.
///
/// Works with URP Lit, URP Unlit, and most standard URP shaders.
/// Creates per-renderer material instances so other objects sharing the same
/// material asset are not affected.
/// </summary>
[DisallowMultipleComponent]
public class DoubleSidedRenderer : MonoBehaviour
{
    private void Awake()
    {
        foreach (Renderer r in GetComponentsInChildren<Renderer>(includeInactive: true))
        {
            // .materials returns instances — changes won't affect the shared asset.
            Material[] mats = r.materials;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i].HasProperty("_Cull"))
                    mats[i].SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
            }
            r.materials = mats;
        }
    }
}
