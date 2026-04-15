using UnityEditor;
using UnityEngine;

public static class SnapToTerrain
{
    [MenuItem("Tools/Snap Selection to Terrain %#s")]  // Ctrl+Shift+S
    static void SnapSelected()
    {
        int snapped = 0;
        int missed = 0;

        foreach (GameObject obj in Selection.gameObjects)
        {
            Vector3 pos = obj.transform.position;

            // Cast ray downward from well above the object
            Ray ray = new Ray(new Vector3(pos.x, pos.y + 500f, pos.z), Vector3.down);

            if (Physics.Raycast(ray, out RaycastHit hit, 1000f))
            {
                Undo.RecordObject(obj.transform, "Snap to Terrain");

                // Find how far the pivot is from the bottom of the mesh
                Renderer[] renderers = obj.GetComponentsInChildren<Renderer>();
                float pivotToBottom = 0f;
                if (renderers.Length > 0)
                {
                    Bounds bounds = renderers[0].bounds;
                    foreach (Renderer r in renderers)
                        bounds.Encapsulate(r.bounds);
                    pivotToBottom = obj.transform.position.y - bounds.min.y;
                }

                obj.transform.position = new Vector3(pos.x, hit.point.y + pivotToBottom, pos.z);
                snapped++;
            }
            else
            {
                missed++;
            }
        }

        if (snapped > 0)
            Debug.Log($"[SnapToTerrain] Snapped {snapped} object(s) to terrain.");
        if (missed > 0)
            Debug.LogWarning($"[SnapToTerrain] {missed} object(s) had no terrain below them.");
    }

    [MenuItem("Tools/Snap Selection to Terrain %#s", true)]
    static bool SnapSelectedValidate() => Selection.gameObjects.Length > 0;
}
