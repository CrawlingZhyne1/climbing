// 런 시드와 청크 시드를 관리한다.
// 청크 좌표별 deterministic random을 생성한다.
// 같은 런의 같은 청크는 같은 배치를 반환한다.
// 다른 런에서는 같은 청크 좌표라도 다른 결과를 만든다.
// 오브젝트 생성과 배치 적용은 다른 매니저가 수행한다.
using System;
using UnityEngine;

public sealed class GameRandomManager : MonoBehaviour
{
    [SerializeField]
    private bool useFixedSeed;

    [SerializeField]
    private int fixedRunSeed = 123456789;

    public int RunSeed { get; private set; }

    public void BeginNewRun()
    {
        if (useFixedSeed)
        {
            RunSeed = fixedRunSeed;
            return;
        }

        unchecked
        {
            int timePart = Environment.TickCount;
            int framePart = Time.frameCount * 73856093;
            int randomPart = UnityEngine.Random.Range(int.MinValue, int.MaxValue);
            RunSeed = MixHash(timePart ^ framePart ^ randomPart);
        }
    }

    public System.Random CreateChunkRandom(int chunkX, int chunkY)
    {
        int seed = GetChunkSeed(chunkX, chunkY);
        return new System.Random(seed);
    }

    public int GetChunkSeed(int chunkX, int chunkY)
    {
        unchecked
        {
            int hash = RunSeed;
            hash ^= MixHash(chunkX * 73856093);
            hash ^= MixHash(chunkY * 19349663);
            return MixHash(hash);
        }
    }

    private static int MixHash(int value)
    {
        unchecked
        {
            uint x = (uint)value;
            x ^= x >> 16;
            x *= 0x7feb352d;
            x ^= x >> 15;
            x *= 0x846ca68b;
            x ^= x >> 16;
            return (int)x;
        }
    }
}
