using UnityEngine;
using UnityEngine.Rendering;

namespace FPSLAN
{
    /// <summary>Printed labels follow the curved white band; three faces keep the logo readable in either hand.</summary>
    public static class SprayCanBranding
    {
        static Mesh s_Label;
        public static void AddTo(Transform can)
        {
            var material = Resources.Load<Material>("Branding/SitaSprayLabel");
            if (material == null) return;
            if (s_Label == null) s_Label = MakeLabel();
            for (int i = 0; i < 3; i++)
            {
                var label = new GameObject("SITA_Print_" + i);
                label.transform.SetParent(can, false);
                label.transform.localRotation = Quaternion.Euler(0, i * 120f, 0);
                label.AddComponent<MeshFilter>().sharedMesh = s_Label;
                var renderer = label.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
        }

        static Mesh MakeLabel()
        {
            const int segments = 16;
            const float radius = 0.058f, halfAngle = 55f * Mathf.Deg2Rad;
            var vertices = new Vector3[(segments + 1) * 2];
            var uv = new Vector2[vertices.Length];
            var triangles = new int[segments * 6];
            // Arc width / original logo aspect ratio (1024:384).
            float height = radius * halfAngle * 2f * 384f / 1024f;
            for (int i = 0; i <= segments; i++)
            {
                float u = i / (float)segments, a = Mathf.Lerp(-halfAngle, halfAngle, u);
                float x = Mathf.Sin(a) * radius, z = Mathf.Cos(a) * radius;
                vertices[i*2] = new Vector3(x, 0.02f - height/2, z);
                vertices[i*2+1] = new Vector3(x, 0.02f + height/2, z);
                uv[i*2] = new Vector2(1f-u, 0); uv[i*2+1] = new Vector2(1f-u, 1);
                if (i == segments) continue;
                int t = i*6, v = i*2;
                triangles[t] = v; triangles[t+1] = v+2; triangles[t+2] = v+1;
                triangles[t+3] = v+2; triangles[t+4] = v+3; triangles[t+5] = v+1;
            }
            var mesh = new Mesh { name = "SITA curved print", vertices = vertices, uv = uv, triangles = triangles };
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }
    }
}
