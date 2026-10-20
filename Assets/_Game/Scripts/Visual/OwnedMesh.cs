using UnityEngine;

namespace FPSLAN
{
    /// <summary>Destrói a malha gerada em runtime quando o objeto é destruído (evita fugas de memória).</summary>
    public class OwnedMesh : MonoBehaviour
    {
        public Mesh mesh;

        void OnDestroy()
        {
            if (mesh != null) Destroy(mesh);
        }
    }
}
