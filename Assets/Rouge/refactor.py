import re

with open('f:/NewRouge/RedVsBlue/Assets/Rouge/RougeGameManager.cs', 'r', encoding='utf-8') as f:
    text = f.read()

# 1. Insert RougeSkillArea struct
struct_def = ""\"
public struct RougeSkillArea
{
    public int Type; // 1=Tornado, 2=Explosion, 3=Laser
    public float2 Position;
    public float2 Direction;
    public float Radius;
    public float Length;
    public float Damage;
    public float PullForce;
    public float SpinForce;
    public float VerticalForce;
}
""\"
text = text.replace('public struct RougeBullet', struct_def + '\npublic struct RougeBullet')


# 2. Modify Simulator Job signature
job_old_fields = ""\"      public float ObstacleLookAhead;
      public float ObstacleRepulsion;
      public float ObstacleOrbitStrength;
      public float TornadoActive;
      public float2 TornadoPos;
      public float TornadoRadius;
      public float TornadoPullForce;
      public float TornadoSpinForce;
      public float BlackHoleActive;
      public float2 BlackHolePos;
      public float BlackHoleRadius;
      public float BlackHolePullForce;
      public float BlackHoleSpinForce;
      public float RenderHeight;
""\"
job_new_fields = ""\"      public float ObstacleLookAhead;
      public float ObstacleRepulsion;
      public float ObstacleOrbitStrength;
      [ReadOnly] public NativeArray<RougeSkillArea> SkillAreas;
      public int SkillAreaCount;
      public float RenderHeight;
""\"
text = text.replace(job_old_fields, job_new_fields)

# 3. Replace Job logic inside Execute
job_old_logic = ""\"            if (TornadoActive > 0.5f)
            {
                float2 diff = TornadoPos - pos.xz;
                float distSq = math.lengthsq(diff);
                if (distSq < tornadoRadiusSq && distSq > 0.0001f)
                {
                    float dist = math.sqrt(distSq);
                    float2 dir = diff / dist;
                    float2 tangent = new float2(-dir.y, dir.x);
                    float weight = 1f - math.saturate(dist / TornadoRadius);
                    acceleration.xz += dir * (TornadoPullForce * weight);
                    acceleration.xz += tangent * (TornadoSpinForce * weight);
                    acceleration.y += 35f * weight;
                }
            }

            if (BlackHoleActive > 0.5f)
            {
                float2 diff = BlackHolePos - pos.xz;
                float distSq = math.lengthsq(diff);
                if (distSq < blackHoleRadiusSq && distSq > 0.0001f)
                {
                    float dist = math.sqrt(distSq);
                    float2 dir = diff / dist;
                    float2 tangent = new float2(-dir.y, dir.x);
                    float weight = 1f - math.saturate(dist / BlackHoleRadius);
                    acceleration.xz += dir * (BlackHolePullForce * weight);
                    acceleration.xz += tangent * (BlackHoleSpinForce * weight);
                }
            }
""\"

job_new_logic = ""\"            for (int s = 0; s < SkillAreaCount; s++)
            {
                RougeSkillArea skill = SkillAreas[s];
                if (skill.Type == 1) // Tornado
                {
                    float2 diff = skill.Position - pos.xz;
                    float distSq = math.lengthsq(diff);
                    if (distSq < skill.Radius * skill.Radius && distSq > 0.0001f)
                    {
                        float dist = math.sqrt(distSq);
                        float weight = 1f - math.saturate(dist / skill.Radius);
                        float2 dir = diff / dist;
                        float2 tangent = new float2(-dir.y, dir.x);
                        acceleration.xz += dir * (skill.PullForce * weight);
                        acceleration.xz += tangent * (skill.SpinForce * weight);
                        acceleration.y += skill.VerticalForce * weight;
                        health -= skill.Damage * DeltaTime;
                    }
                }
                else if (skill.Type == 2) // Explosion (instant huge push + dmg)
                {
                    float2 diff = pos.xz - skill.Position; // Push away
                    float distSq = math.lengthsq(diff);
                    if (distSq < skill.Radius * skill.Radius && distSq > 0.0001f)
                    {
                        float dist = math.sqrt(distSq);
                        float weight = 1f - math.saturate(dist / skill.Radius);
                        float2 dir = diff / dist;
                        acceleration.xz += dir * (skill.PullForce * weight); // pullForce applied as outward if signed so
                        acceleration.y += skill.VerticalForce * weight;
                        health -= skill.Damage; // Assuming lasts 1 frame, raw dmg
                    }
                }
                else if (skill.Type == 3) // Laser
                {
                    float2 pToS = pos.xz - skill.Position;
                    float dot = math.dot(pToS, skill.Direction);
                    if (dot > 0f && dot < skill.Length)
                    {
                        float2 proj = skill.Position + skill.Direction * dot;
                        float distSq = math.lengthsq(pos.xz - proj);
                        if (distSq < skill.Radius * skill.Radius)
                        {
                            float weight = 1f - math.saturate(math.sqrt(distSq) / skill.Radius);
                            health -= skill.Damage * DeltaTime;
                            acceleration.xz += skill.Direction * (skill.PullForce * weight);
                            acceleration.y += skill.VerticalForce * weight;
                        }
                    }
                }
            }
""\"
text = text.replace(job_old_logic, job_new_logic)

with open('f:/NewRouge/RedVsBlue/Assets/Rouge/RougeGameManager.cs', 'w', encoding='utf-8') as f:
    f.write(text)

