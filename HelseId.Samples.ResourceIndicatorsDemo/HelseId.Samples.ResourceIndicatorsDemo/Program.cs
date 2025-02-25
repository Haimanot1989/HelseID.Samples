﻿using IdentityModel;
using IdentityModel.Client;
using IdentityModel.OidcClient;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.Tokens;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.IO;
using System.Net.Http;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace HelseId.RefreshTokenDemo
{
    /*
     * This sample application shows how Resource Indicators 
     * (https://datatracker.ietf.org/doc/html/rfc8707)
     * to download multiple Access Tokens without performing multiple
     * calls to the Authorize endpoint.
     * This is important when calling national health APIs since most
     * of these require Access Tokens where they are the only audience.
     * 
     * This sample app has access to two APIs: udelt:resource_indicator_api_1
     * and udelt:resource_indicator_api_2. The app requests scopes from 
     * both APIs but the first Token-call only requests the first resource.
     * The second Token-call (using a Refresh Token) requests the second
     * resource.
     */
    class Program
    {
        const string ClientId = "ro-demo";
        const string Localhost = "http://localhost:8089";
        const string RedirectUrl = "/callback";
        const string StartPage = "/start";
        const string StsUrl = "https://helseid-sts.test.nhn.no";

        const string firstResource = "e-helse:sfm.api";
        const string secondResource = "kjernejournal.api";


        static async Task Main()
        {
            try
            {
                var httpClient = new HttpClient();
                var disco = await httpClient.GetDiscoveryDocumentAsync(StsUrl);
                if (disco.IsError)
                {
                    throw new Exception(disco.Error);
                }

                // 1. Logging in the user
                // ///////////////////////
                // Perfom user login, uses the /authorize endpoint in HelseID
                // Use the Resource-parameter to indicate which API-s you want tokens for
                // Use the Scope-parameter to indicate which scopes you want for these API-s

                var clientAssertionPayload = GetClientAssertionPayload(ClientId, disco);
                var oidcClient = new OidcClient(new OidcClientOptions
                {                    
                    Authority = StsUrl,
                    LoadProfile = false,
                    RedirectUri = "http://localhost:8089/callback",
                    Scope = "openid profile offline_access e-helse:sfm.api/sfm.api https://ehelse.no/kjernejournal/kj_api",
                    ClientId = ClientId,
                    Resource = new List<string> { firstResource, secondResource },
                    ClientAssertion = clientAssertionPayload,                   

                    Policy = new Policy { ValidateTokenIssuerName = true },                    
                });

                var state = await oidcClient.PrepareLoginAsync();
                var response = await RunLocalWebBrowserUntilCallback(Localhost, RedirectUrl, StartPage, state);



                // 2. Retrieving an access token for API 1, and a refresh token
                ///////////////////////////////////////////////////////////////////////
                // User login has finished, now we want to request tokens from the /token endpoint
                // We add a Resource parameter indication that we want scopes for API 1
                var parameters = new Parameters
                {
                    { "resource", firstResource }
                };
                var loginResult = await oidcClient.ProcessResponseAsync(response, state, parameters);

                if (loginResult.IsError)
                {
                    throw new Exception(loginResult.Error);
                }
                var accessToken1 = loginResult.AccessToken;
                var refreshToken = loginResult.RefreshToken;


                Console.WriteLine("First request, resource: " + firstResource);
                Console.WriteLine("Access Token: " + accessToken1);
                Console.WriteLine("Refresh Token: " + refreshToken);
                Console.WriteLine();



                // 3. Using the refresh token to get an access token for API 2
                //////////////////////////////////////////////////////////////
                // Now we want a second access token to be used for API 2
                // Again we use the /token-endpoint, but now we use the refresh token
                // The Resource parameter indicates that we want a token for API 2.
                var refreshTokenRequest = new RefreshTokenRequest
                {
                    Address = disco.TokenEndpoint,
                    ClientId = ClientId,
                    RefreshToken = refreshToken,
                    Resource = new List<string> { secondResource },
                    ClientAssertion = GetClientAssertionPayload(ClientId, disco)
                };

                var refreshTokenResult = await httpClient.RequestRefreshTokenAsync(refreshTokenRequest);

                if (refreshTokenResult.IsError)
                {
                    throw new Exception(refreshTokenResult.Error);
                }

                Console.WriteLine("Second request, resource: " + secondResource);
                Console.WriteLine("Access Token: " + refreshTokenResult.AccessToken);
                Console.WriteLine("Refresh Token: " + refreshTokenResult.RefreshToken);
                Console.WriteLine();

            }
            catch (Exception e)
            {
                Console.Error.WriteLine("Error:");
                Console.Error.WriteLine(e.ToString());
            }
        }

        private static ClientAssertion GetClientAssertionPayload(string clientId, DiscoveryDocumentResponse disco)
        {
            var clientAssertionString = BuildClientAssertion(clientId, disco);
            return new ClientAssertion
            {
                Type = OidcConstants.ClientAssertionTypes.JwtBearer,
                Value = clientAssertionString
            };

        }

        private static string BuildClientAssertion(string clientId, DiscoveryDocumentResponse disco)
        {
            var claims = new List<Claim>
            {
                new Claim(JwtClaimTypes.Subject, clientId),
                new Claim(JwtClaimTypes.IssuedAt, DateTimeOffset.Now.ToUnixTimeSeconds().ToString()),
                new Claim(JwtClaimTypes.JwtId, Guid.NewGuid().ToString("N")),
            };

            var credentials = new JwtSecurityToken(clientId, disco.TokenEndpoint, claims, DateTime.UtcNow, DateTime.UtcNow.AddSeconds(60), GetClientAssertionSigningCredentials());

            var tokenHandler = new JwtSecurityTokenHandler();
            return tokenHandler.WriteToken(credentials);
        }

        private static SigningCredentials GetClientAssertionSigningCredentials()
        {
            //var jwk = File.ReadAllText("jwk.json");
            //var securityKey = new JsonWebKey(jwk);
            return new SigningCredentials(GetSecurityKey(), SecurityAlgorithms.RsaSha256);
        }

        private static SecurityKey GetSecurityKey()
        {
            // TODO: Store the RSA key in a secure location!!
            const string rsaPrivateKey = "<RSAKeyValue><Modulus>sHNMAYJkqAj9970orrqHgjPD0l+PgqVnureLgOvYffUs0NzkQXAlg1L8Kj3eZkldVdW7aTUnvBDtJfw/Ad0XxH00OkV9Lha9ewpJAGchz/bIp6j+GkzYajys6du9d8MJg8VQY3X+9MTjtAH6Kf1wzXE+7fRGFT2PkN/DedwT2KDTwWNOYk9uILka4QLrzonu2TL2Hme82fn744JuPIsV7DTJ9zEoxD2dziywsFz0Rg4KNNQaL+O4HI9tuQx9ivO7hdcgGOy4lCI2U8Kf27O3txW/Jkh7SMgpGL3k+Xb+uvQKuSgeqtpQublm78A3c1vLqepGD4ccuCZ+XSCzqKCn/4CmpFRL8psT1WsWGYbuCU4Ih18viKgOqxaFOHgOC4NnRR0FoUXBdPK1Q3HLHGoPoUV47PNDaasJRVwZWBA7MQICC2mrvPpkcmoDwrsAAcGvY9YOb2e04tMjHvTYUsP+9pk1kfy1N/3hjWCpJDX8i44pWD6eSmTkpQX9lK6HigEPq414tOd4EzfxcNRAfMkg/OKLkaORdG1WKsrOoo8pCPUqTdq72JhYFJ0/vYDzRIWAphm9VM0KDP5lnK2fXku5Kn+5m8u6NJHWxBlm47OEhyWo6r1Z9hdvhXUREti+RnqQsqRzeDn46XCwuKaVIhrShXoViiiFs82DslMDDExUjm0=</Modulus><Exponent>AQAB</Exponent><P>yubldftOSBBcQEXizaxjK2aHwnGOiz4obUT9+mepWe1G/Ev3iG627rA8l2+MSvP/DJEyhypDk5sx7BLpW4oQqBJcUoigaD13OWuUQe52vDcTQlkTrAPSS0xOODISEJ4nAgzAPgoYDvcCYDF61S83LMudQnwmxgdkpkspcfbgiZeNFCPo3W2CKh2GXuvNpk9XDmJ72Vl9g9+rTJl6P2XnjHBy5knSBKWDJI3Zt+waBoQzAkgjsAi9wncc7rxx/eurwp8B7lqoX/Nne+oHZZ3OvRHn5ht10r3qsyQEUfz/TQ74li17IS5o0Sqf5jSFaBUUkhGJiu2AsTkuv2nPtYEyIw==</P><Q>3qBRpO/614MI8zuSl7RvIIaFW+HLNXf3dWC2h32WFLD384BzjD3avyjeTSWsGV+poVevpixnwM7KGK4FtKakynSKHPeIa8twcE+4kOIIVjmwbz4zGOW81Mnfvh8Ee1iLKP81IsaG+nPAZKkTbE5hjEvCP8bLb1gRbNjWOAc+mtPUx4WSjUoTcdbPY3ktO7ZSTD8tsdJ2sTN2ZEwdQ22+BftFTxcOC1J+rAbDeIkk31V2Hf0a8V9RZK15I8jUxH4EtErZ018Ay+tG+tegVSzKcsyyfx1FfHLwqcASfNT1JMS3iFZ7LEacN/IK3drnBhu/d5NCvFOWhHePbFrJHHbeLw==</Q><DP>xqtyviUFL1alnWFQhCZpK9PG1kMuWXTRTLyjGo5pqd3FBcC0bOhLQkdZ7MWSTsm+T+XT3bkqVds99HNH/xOe35Kqxz10It0cYiLOFgiSRhR/TRW/R0yumn/qjuen/JF+jGlDyvtDN1PxBZMtPJRwp/Hu12yM4pXWnWU2/ZnHnbHAt5m5pyZUrzwdl8+3m0JQcYtIzTbsyTU2m1gj9POo10A7oPVjKJ2PXTlvlsEdcof7Eh7korbMZx8OO0xVKVWa5oOe9m3aM6k3CIPMHll4VnSz5gG5SlIe/q0jdcwNhrxD93gs+f5hL31W96cxgQozDBsT2+5VdjIRbecDNCt+lQ==</DP><DQ>Q5X4M1KHnJWzSeR0BIpKkl1EbziFMJ5TCddqkoeV4II5RDti2NiOaCpIErO1I57fKJQuRwyEEwy0Xfm20bklnjDzHQgo6lDAudf5+EImtcadwafoa06TnSYMPvO7sJaY6MFRqFUM9UvexLBvrRm+k5EMT8BSUmMyJxFNN4U7hFV663epnis27ACCxXgsO0yGf49OmAWE8xbkgl55I9dVMQuvZutg4B8TRbZn8VfxUbvoOAJ3A4AkfaQMesilj2GSnAl9R6Y337B1xAFiM3l9nIx4RA7m4XkjhuVAt5UPNzJhZYqbqj1lf7aDhgbGzBvwbKTQRcw6jcyeRg7przKHEQ==</DQ><InverseQ>C8z88QY93r/05id2daU9obsIEe7R1bjUHNj+3rKo8T+L8PUoXuWQTm/NsQryoSgi5/JwZL7gyh7IQDPDFbf4jWg6nZ8ZfJs0Qisjih3cPjPMIxYvi0bG38Z1RECysNqBDTNrULMHIScOA+BhvnSPoXGQU8vJTO4yjH7V4wFcE6J9qcPPAUSy/KtgWRd91JWH/oX7PUgUDMVWc3hQ8RTyPCl60G7pFjeKhSqhRzfXIF45AmfSlOTY2l9aO1swp/cQsebym96AkYA71q2c+08KZvERvUuS0FGpZ7VQSgZ+sUe8WZb9XzJXdirtuU/sz74BFwTiT9YkoGC9hH1aMDBiFA==</InverseQ><D>sGNxtYiN6tSiXUeBJbpdwDDTLrhMlAOZgDP/hu89Sh0PofNPUoMzXOZWIjwa2RG59hZk9LUodX5OM0zIB6rnGYs37JCOpMYiwJ71fyuZx3Uh/UiYS95J8VmaWWVLMC+OkWVsCSFpr3IrVkUruVIbs6PjjqhEbvNNUzv9AxKX3FRZmtcVAn34z0l7rzfmVl/YntOs6ZQ2W4jk3vgCDw/S6H+U7kD8ScB2wiY2svcZUfazCUCGtRzlbdeLjhMIZSFlclQtR/1MPvk8adsDRvOPUbyxiyml5IoDWzJpdWAZIPbYyWNr1MvNKvxGBKGYTP+UxtTlGJyufwAsDhikwItpo/2q8tsK4CIPsR4+vxyzOCwpH7s64MBv6Sf/5sDq46hblIscyCmkgdTSaM9Q8hMxZz7LxZk6IsQhE4X3YW+jCwXRypMzgTQfCsNRLFzhMbjR1/DcFYk3zQIOi9NFiVNSIGYnvuCwUp7/KWc7yvfQ+Bs7jfVtMui+MMKf87HHVUYgDXNsfkFRg7+t86OUVXWcAZK6p0PMI7MagDyglGTN7z5E2v+jwxNBR0nGP9V3RVPl8LnJ7A/OLh1CacIfASxIgOvSEl1tUeyrZaVjnGH2LAUrK8oN3d9TlWH5hjK9RPxrRdxyIuU2q9tHS+IIXCaotOJ8MbTBi/DR9zRL39CQKZk=</D></RSAKeyValue>";
            var rsa = RSA.Create();
            rsa.FromXmlString(rsaPrivateKey);

            return new RsaSecurityKey(rsa.ExportParameters(true));
        }

        private static async Task<string> RunLocalWebBrowserUntilCallback(string localhost, string redirectUrl, string startPage, AuthorizeState state)
        {
            // Build a HTML form that does a POST of the data from the url
            // This is a workaround since the url may be too long to pass to the browser directly
            var startPageHtml = UrlToHtmlForm.Parse(state.StartUrl);

            // Setup a temporary http server that listens to the given redirect uri and to 
            // the given start page. At the start page we can publish the html that we
            // generated from the StartUrl and at the redirect uri we can retrieve the 
            // authorization code and return it to the application
            var listener = new ContainedHttpServer(localhost, redirectUrl,
                new Dictionary<string, Action<HttpContext>> {
                    { startPage, async ctx => await ctx.Response.WriteAsync(startPageHtml) }
                });

            RunBrowser(localhost + startPage);

            return await listener.WaitForCallbackAsync();
        }

        private static void RunBrowser(string url)
        {
            // Thanks Brock! https://brockallen.com/2016/09/24/process-start-for-urls-on-net-core/
            url = url.Replace("&", "^&");
            Process.Start(new ProcessStartInfo("cmd", $"/c start {url}") { CreateNoWindow = true });
        }
    }
}
