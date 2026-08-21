using KiloExtensionDTOs.Profile;
using KiloVisualStudioExtension.ApiClient;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KiloVisualStudioExtension.Utils
{
  public class EntityConverter
  {
    public static ProfileData Convert(Response23 response)
    {
      return new ProfileData
      {
        Profile = response.Profile,
        Balance = Convert(response.Balance),
        KiloPass = Convert(response.KiloPass),
        CurrentOrgId = Convert(response.CurrentOrgId)
      };
    }

    public static object ConvertToDTO(Profile profile)
    {
      var result = new 
      {
        Email = profile.Email,
        Name = profile.Name,
        Organizations = profile.Organizations?.Select(o => Convert(o)).ToList(),
        SelectedOrganizationId = profile.SelectedOrganizationId,
        HasPersonalAccount = profile.HasPersonalAccount
      },
    }

    public static string Convert(CurrentOrgId orgId)
    {
      if (orgId == null) return null;
      return orgId.Value;
    }

    public static KilocodeBalance Convert(Balance balance)
    {
      if (balance == null) return null;
      return new KilocodeBalance
      {
        Balance = balance.balance
      };
    }

    public static KiloPassState Convert(KiloPass kilopass)
    {
      if (kilopass == null) return null;
      return new KiloPassState
      {
        CurrentPeriodBaseCreditsUsd = kilopass.CurrentPeriodBaseCreditsUsd,
        CurrentPeriodBonusCreditsUsd = kilopass.CurrentPeriodBonusCreditsUsd,
        CurrentPeriodUsageUsd = kilopass.CurrentPeriodUsageUsd,
        NextBillingAt = kilopass.NextBillingAt
      };
    }

    public static ProfileData Convert(Response23 response)
    {
      KilocodeBalance? balance = null;
      if (response.Balance?.AdditionalProperties?.ContainsKey("balance") == true)
      {
        var balanceValue = response.Balance.AdditionalProperties["balance"];
        balance = new KilocodeBalance
        {
          Balance = balanceValue is double d ? d : Convert.ToDouble(balanceValue)
        };
      }

      KiloPassState? kiloPass = null;
      if (response.KiloPass?.AdditionalProperties?.Count > 0)
      {
        kiloPass = new KiloPassState
        {
          CurrentPeriodBaseCreditsUsd = response.KiloPass.AdditionalProperties.TryGetValue("currentPeriodBaseCreditsUsd", out var bcp)
                ? (bcp is double bd ? bd : Convert.ToDouble(bcp))
                : 0,
          CurrentPeriodUsageUsd = response.KiloPass.AdditionalProperties.TryGetValue("currentPeriodUsageUsd", out var cpu)
                ? (cpu is double cu ? cu : Convert.ToDouble(cpu))
                : 0,
          CurrentPeriodBonusCreditsUsd = response.KiloPass.AdditionalProperties.TryGetValue("currentPeriodBonusCreditsUsd", out var bnc)
                ? (bnc is double bn ? bn : Convert.ToDouble(bnc))
                : 0,
          NextBillingAt = response.KiloPass.AdditionalProperties.TryGetValue("nextBillingAt", out var nba)
                ? nba?.ToString()
                : null
        };
      }

      string? currentOrgId = null;
      if (response.CurrentOrgId?.AdditionalProperties?.Count > 0)
      {
        var orgIdValue = response.CurrentOrgId.AdditionalProperties.FirstOrDefault().Value;
        currentOrgId = orgIdValue?.ToString();
      }

      return new ProfileData
      {
        Profile = new KiloExtensionDTOs.Profile.Profile
        {
          Email = response.Profile.Email,
          Name = response.Profile.Name,
          Organizations = response.Profile.Organizations?.Select(o => new KiloExtensionDTOs.Profile.Organization
          {
            Id = o.Id,
            Name = o.Name,
            Role = o.Role
          }).ToList(),
          SelectedOrganizationId = response.Profile.SelectedOrganizationId,
          HasPersonalAccount = response.Profile.HasPersonalAccount
        },
        Balance = balance,
        KiloPass = kiloPass,
        CurrentOrgId = currentOrgId
      };
    }
  }
}
