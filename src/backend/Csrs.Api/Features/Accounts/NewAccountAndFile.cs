﻿using Csrs.Api.Models;
using Csrs.Api.Repositories;
using Csrs.Api.Services;
using Csrs.Interfaces.Dynamics;
using Csrs.Interfaces.Dynamics.Models;
using MediatR;
using File = Csrs.Api.Models.File;

namespace Csrs.Api.Features.Accounts
{
    public static class NewAccountAndFile
    {
        public class Request : IRequest<Response>
        {
            public Request(Party applicant, File file)
            {
                Applicant = applicant ?? throw new ArgumentNullException(nameof(applicant));
                File = file ?? throw new ArgumentNullException(nameof(file));
            }

            public Party Applicant { get; init; }
            public File File { get; init; }
        }

        public class Response
        {
            public Response(string partyId, string fileId, string fileNumber)
            {
                PartyId = partyId;
                FileId = fileId;
                FileNumber = fileNumber;
            }

            public string PartyId { get; init; }
            public string FileId { get; init; }
            public string FileNumber { get; init; }
        }

        public class Handler : IRequestHandler<Request, Response>
        {
            private readonly IDynamicsClient _dynamicsClient;
            private readonly IUserService _userService;
            private readonly IAccountService _accountService;
            private readonly IFileService _fileService;

            private readonly ILogger<Handler> _logger;

            public Handler(
                IDynamicsClient dynamicsClient,
                IUserService userService, 
                IAccountService accountService,
                IFileService fileService, 
                ILogger<Handler> logger)
            {
                _dynamicsClient = dynamicsClient ?? throw new ArgumentNullException(nameof(dynamicsClient));
                _userService = userService ?? throw new ArgumentNullException(nameof(userService));
                _accountService = accountService ?? throw new ArgumentNullException(nameof(accountService));
                _fileService = fileService ?? throw new ArgumentNullException(nameof(fileService));
                _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            }

            public async Task<Response> Handle(Request request, CancellationToken cancellationToken)
            {
                _logger.LogDebug("Checking current user for BCeID Guid");

                //string userId = _userService.GetBCeIDUserId();
                string userId = "26336072-cba4-4b6e-871b-5355d27df9b3";
                if (string.IsNullOrEmpty(userId))
                {
                    _logger.LogInformation("No BCeID on authenticated user, cannot create account");
                    return new Response(string.Empty, string.Empty, string.Empty);
                }

                var bceidScope = _logger.AddBCeIdGuid(userId);

                var dynamicsParty = await request.Applicant.ToDynamicsModelAsync(_dynamicsClient, cancellationToken);

                // find to see if the person has an account already?
                string partyId = await _dynamicsClient.GetPartyIdByBCeIdAsync(userId, cancellationToken);
                
                if (partyId is not null)
                {
                    _logger.LogDebug("Party already exists");
                    dynamicsParty.SsgCsrspartyid = partyId;

                    try
                    {
                        await _dynamicsClient.Ssgcsrsparties.UpdateAsync(partyId, dynamicsParty, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Exception in Handle.updating party");
                    }
                }
                else
                {
                    // will case party to be created
                    _logger.LogDebug("Party does not exist, create new party");
                    dynamicsParty.SsgBceidGuid = userId;
                    dynamicsParty.SsgBceidLastUpdate = DateTimeOffset.Now;
                    dynamicsParty.Statuscode = 1;

                    try
                    {
                        dynamicsParty = await _dynamicsClient.Ssgcsrsparties.CreateAsync(body: dynamicsParty, cancellationToken: cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Exception in Handle.creating party");
                    }

                    partyId = dynamicsParty.SsgCsrspartyid;
                }

                // create the other party
                MicrosoftDynamicsCRMssgCsrsparty otherDynamicsParty = new MicrosoftDynamicsCRMssgCsrsparty();
                if (request.File.OtherParty != null)
                {
                    request.File.OtherParty.PartyId = Guid.Empty.ToString();
                    otherDynamicsParty = await request.File.OtherParty.ToDynamicsModelAsync(_dynamicsClient, cancellationToken);
                    _logger.LogInformation("Creating other party");
                    try
                    {
                        otherDynamicsParty = await _dynamicsClient.Ssgcsrsparties.CreateAsync(body: otherDynamicsParty, cancellationToken: cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Exception in Handle.creating other party");
                    }

                }
                else
                {
                    _logger.LogInformation("No other party provided");
                }

                // create the file

                var file = await _fileService.CreateFile(dynamicsParty, otherDynamicsParty, request.File, cancellationToken);

                _logger.LogDebug("Party and file created successfully");
                return new Response(partyId, file.Item1, file.Item2);
            }
        }
    }
}
