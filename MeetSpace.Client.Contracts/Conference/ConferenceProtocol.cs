using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MeetSpace.Client.Contracts.Conference;

public static class ConferenceProtocol
{
    public const string Object = "conference";

    public static class Agents
    {
        public const string Lifecycle = "lifecycle";
        public const string Membership = "membership";
    }

    public static class Actions
    {
        public const string CreateConference = "create_conference";
        public const string GetConference = "get_conference";
        public const string CloseConference = "close_conference";
        public const string JoinConference = "join_conference";
        public const string LeaveConference = "leave_conference";
        public const string ListMembers = "list_members";
    }
}