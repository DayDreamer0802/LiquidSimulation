using UnityEngine;
using UnityEngine.Rendering;

public sealed partial class RougeGameManager
{
    private const int ContactShadowBatchSize = 1023;
    private static readonly int EnemyTypeSizesId = Shader.PropertyToID("_EnemyTypeSizes");
    private static readonly int GroundHeightId = Shader.PropertyToID("_GroundHeight");
    private static readonly int InstanceDensityId = Shader.PropertyToID("_InstanceDensity");
    private static readonly Quaternion ContactShadowRotation = Quaternion.Euler(90f, 0f, 0f);

    private readonly Matrix4x4[] _contactShadowMatrices =
        new Matrix4x4[ContactShadowBatchSize];
    private Material _enemyContactShadowMaterial;
    private Material _contactShadowMaterial;

    private void InitializeLightingVisuals()
    {
        DisposeLightingVisuals();
        Shader enemyShadow = FindRuntimeShader("Rouge/Enemy Contact Shadow");
        if (enemyShadow != null)
        {
            _enemyContactShadowMaterial = new Material(enemyShadow)
            {
                name = "Runtime Enemy Contact Shadows",
                hideFlags = HideFlags.DontSave,
                enableInstancing = true
            };
        }

        Shader contactShadow = FindRuntimeShader("Rouge/Contact Shadow");
        if (contactShadow != null)
        {
            _contactShadowMaterial = new Material(contactShadow)
            {
                name = "Runtime Tower Contact Shadows",
                hideFlags = HideFlags.DontSave,
                enableInstancing = true
            };
        }
    }

    private void DisposeLightingVisuals()
    {
        if (_enemyContactShadowMaterial != null) Destroy(_enemyContactShadowMaterial);
        if (_contactShadowMaterial != null) Destroy(_contactShadowMaterial);
        _enemyContactShadowMaterial = null;
        _contactShadowMaterial = null;
    }

    private void RenderContactShadows(int enemyDrawCount, Bounds enemyBounds)
    {
        if (enemyMesh == null) return;
        if (RougeVisualQualityManager.StaticContactShadowsEnabled)
            RenderStaticContactShadows();
        if (!RougeVisualQualityManager.EnemyContactShadowsEnabled) return;
        if (enemyDrawCount <= 0 || _enemyContactShadowMaterial == null ||
            _argsBuffer == null || _positionBuffer == null || _stateBuffer == null ||
            _enemyKindRenderBuffer == null)
            return;

        _enemyContactShadowMaterial.SetBuffer(PositionScaleBufferId, _positionBuffer);
        _enemyContactShadowMaterial.SetBuffer("_StateBuffer", _stateBuffer);
        _enemyContactShadowMaterial.SetBuffer("_EnemyKindBuffer", _enemyKindRenderBuffer);
        _enemyContactShadowMaterial.SetVector(ScaleMultiplierId,
            new Vector4(enemySpriteSize.x, enemySpriteSize.y, 0f, 0f));
        if (enemyMaterial != null && enemyMaterial.HasProperty(EnemyTypeSizesId))
            _enemyContactShadowMaterial.SetVector(EnemyTypeSizesId,
                enemyMaterial.GetVector(EnemyTypeSizesId));
        _enemyContactShadowMaterial.SetFloat(GroundHeightId, GetContactShadowGroundHeight());
        _enemyContactShadowMaterial.SetFloat(InstanceDensityId,
            RougeVisualQualityManager.EnemyShadowDensity);

        Graphics.DrawMeshInstancedIndirect(
            enemyMesh,
            0,
            _enemyContactShadowMaterial,
            enemyBounds,
            _argsBuffer,
            0,
            null,
            ShadowCastingMode.Off,
            false,
            gameObject.layer);
    }

    private void RenderStaticContactShadows()
    {
        if (_contactShadowMaterial == null || enemyMesh == null) return;

        int count = 0;
        if (mainTower != null && mainTower.isActiveAndEnabled)
            AppendContactShadow(mainTower.transform.position, 5.2f, ref count);

        for (int i = 0; i < _defenseTowers.Count; i++)
        {
            RougeDefenseTower tower = _defenseTowers[i];
            if (tower == null || !tower.isActiveAndEnabled) continue;
            AppendContactShadow(tower.transform.position, GetTowerShadowDiameter(tower), ref count);
        }

        if (_towerPreview != null && _towerPreview.isActiveAndEnabled &&
            !_defenseTowers.Contains(_towerPreview))
            AppendContactShadow(_towerPreview.transform.position,
                GetTowerShadowDiameter(_towerPreview), ref count);

        if (player != null && player.isActiveAndEnabled)
            AppendContactShadow(player.transform.position, 1.85f, ref count);

        if (_bossSpriteAnimator != null && _bossSpriteAnimator.isActiveAndEnabled)
            AppendContactShadow(_bossSpriteAnimator.transform.position, 5.8f, ref count);

        FlushContactShadowBatch(ref count);
    }

    private void AppendContactShadow(Vector3 sourcePosition, float diameter, ref int count)
    {
        if (count >= _contactShadowMatrices.Length) FlushContactShadowBatch(ref count);
        Vector2 direction = RougeVisualQualityManager.ShadowDirection;
        float safeDiameter = Mathf.Max(0.2f, diameter);
        Vector3 position = new Vector3(
            sourcePosition.x + direction.x * safeDiameter * 0.1f,
            GetContactShadowGroundHeight(),
            sourcePosition.z + direction.y * safeDiameter * 0.1f);
        _contactShadowMatrices[count++] = Matrix4x4.TRS(
            position,
            ContactShadowRotation,
            new Vector3(safeDiameter, safeDiameter * 0.62f, 1f));
    }

    private void FlushContactShadowBatch(ref int count)
    {
        if (count <= 0) return;
        Graphics.DrawMeshInstanced(
            enemyMesh,
            0,
            _contactShadowMaterial,
            _contactShadowMatrices,
            count,
            null,
            ShadowCastingMode.Off,
            false,
            gameObject.layer,
            null,
            LightProbeUsage.Off,
            null);
        count = 0;
    }

    private float GetContactShadowGroundHeight()
    {
        return Mathf.Max(0.03f, renderHeight * 0.43f);
    }

    private static float GetTowerShadowDiameter(RougeDefenseTower tower)
    {
        return tower.TowerType switch
        {
            RougeTowerType.Cannon => 3.15f,
            RougeTowerType.OrbitSphere => 3.2f,
            RougeTowerType.RocketBarrage => 3.1f,
            RougeTowerType.ChargeTower => 3.25f,
            RougeTowerType.ReinforcementTower => 3.25f,
            RougeTowerType.MachineGun => 2.55f,
            RougeTowerType.Laser => 2.55f,
            _ => 2.85f
        };
    }
}
