using System;
using System.Threading.Tasks;
using UnityEngine;
using Unity.Services.Core;
using Unity.Services.Authentication;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;

public static class RelayManager
{
    private static bool _initialized;

    public static async Task InitializeAsync()
    {
        if (_initialized) return;

        try
        {
            await UnityServices.InitializeAsync();

            if (!AuthenticationService.Instance.IsSignedIn)
                await AuthenticationService.Instance.SignInAnonymouslyAsync();

            _initialized = true;
            Debug.Log("[Relay] Unity Services initialized + signed in anonymously");
        }
        catch (Exception e)
        {
            Debug.LogError($"[Relay] Failed to initialize Unity Services: {e.Message}");
            throw;
        }
    }

    public static async Task<(Allocation allocation, string joinCode)> AllocateRelayAsync(int maxPlayers)
    {
        var allocation = await RelayService.Instance.CreateAllocationAsync(maxPlayers);
        var joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
        Debug.Log($"[Relay] Allocated relay — join code: {joinCode}");
        return (allocation, joinCode);
    }

    public static async Task<JoinAllocation> JoinRelayAsync(string joinCode)
    {
        var joinAllocation = await RelayService.Instance.JoinAllocationAsync(joinCode);
        Debug.Log($"[Relay] Joined relay via code: {joinCode}");
        return joinAllocation;
    }
}
