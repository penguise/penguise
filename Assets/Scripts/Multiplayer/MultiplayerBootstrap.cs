using System;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;
using UnityEngine;

public class MultiplayerBootstrap : MonoBehaviour
{
    private Task initializationTask;

    public Task InitializeAsync()
    {
        if (initializationTask == null || initializationTask.IsFaulted)
            initializationTask = InitializeServicesAsync();
        return initializationTask;
    }

    private async void Start()
    {
        try
        {
            await InitializeAsync();
        }
        catch (Exception exception)
        {
            Debug.LogException(exception, this);
        }
    }

    private async Task InitializeServicesAsync()
    {
        await UnityServices.InitializeAsync();

        if (!AuthenticationService.Instance.IsSignedIn)
        {
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }
    }
}
