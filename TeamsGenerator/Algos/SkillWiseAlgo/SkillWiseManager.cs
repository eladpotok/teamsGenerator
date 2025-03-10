﻿using System;
using System.Collections.Generic;
using System.Linq;
using TeamsGenerator.Orchestration;
using TeamsGenerator.Orchestration.Contracts;
using TeamsGenerator.Utilities;

namespace TeamsGenerator.Algos.SkillWiseAlgo
{
    public class SkillWiseManager : AlgoManagerBase, IAlgoManager
    {
        private List<SkillWisePlayer> _players;

        public SkillWiseManager(AlgoConfig config) : base(config)
        {
        }

        public List<Team> GenerateTeams(List<IPlayer> players, List<Team> generatedTeamWithLockedPlayers)
        {
            _players = new List<SkillWisePlayer>(players.Cast<SkillWisePlayer>());

            var teams = new List<Team>();

            for (int i = 0; i < _config.TeamsCount; i++)
            {
                teams.Add(new Team());

            }

            if (generatedTeamWithLockedPlayers != null)
            {
                var teamIndex = 0;
                foreach (var team in generatedTeamWithLockedPlayers)
                {
                    foreach(var player in team.Players)
                    {
                        teams[teamIndex].AddPlayer(player);
                    }
                    players = players.Where(pl => !team.Players.Select(t => t.Key).Contains(pl.Key)).ToList();
                    teamIndex++;
                }

                teams = teams.OrderBy(t => t.Players.Count).ThenBy(t => t.TotalRank).ToList();
            }

            return RunAlgo(ref teams, players.Cast<SkillWisePlayer>().ToList());
        }

        private List<Team> RunAlgo(ref List<Team> teams, List<SkillWisePlayer> players)
        {
            var allTypesOfSkills = GetSkillsRandomOrder();
       
            for (int i = 0; i < _players.Count; i++)
            {
                if (!allTypesOfSkills.Any())
                {
                    allTypesOfSkills = GetSkillsRandomOrder();
                }

                //players = Helper.SpreadGoalKeepersInDifferentTeams(teams, players.Cast<IPlayer>().ToList()).Cast<SkillWisePlayer>().ToList();
                AddSkillPlayerToTeam(ref teams, players, TakeRandomSkill(allTypesOfSkills));
            }
            
            return teams;
        }

        private static List<OrderBy> GetSkillsRandomOrder()
        {
            var allTypesOfSkills = new List<OrderBy>()
            {
                new OrderBy() { Name="Leadership", Invoker = t => t.Leadership },
                new OrderBy() { Name="Attack", Invoker = t => t.Attack },
                new OrderBy() { Name="Defence", Invoker = t => t.Defence },
                new OrderBy() { Name="Stamina", Invoker = t => t.Stamina },
                new OrderBy() { Name="Passing", Invoker = t => t.Passing },
            };

            allTypesOfSkills = Helper.Shuffle(allTypesOfSkills);
            return allTypesOfSkills;
        }

        private Func<SkillWisePlayer, double> TakeRandomSkill(List<OrderBy> skills)
        {
            var random = new Random();
            var index = random.Next(0, skills.Count);
            var skill = skills[index];
            Log($"Using skill {skill.Name}");
            skills.RemoveAt(index);
            return skill.Invoker;
        }

        private void AddSkillPlayerToTeam(ref List<Team> teams, List<SkillWisePlayer> playersLeft, Func<SkillWisePlayer, double> orderBy)
        {
            var orderedPlayers = OrderByAndShuffleSequence(playersLeft, orderBy);
            for (int i = 0; i < _config.TeamsCount; i++)
            {
                if (!playersLeft.Any()) return;
                
                // if not all teams has the same players count, and this team has reached the limit. we skip for padding all other teams.
                if (teams[i].Players.Count == Math.Ceiling((double)_players.Count / _config.TeamsCount)) continue;

                teams[i].AddPlayer(TakePlayer(orderedPlayers.Count - 1, orderedPlayers, playersLeft));
            }
            teams = teams.OrderBy(t => t.Players.Count).OrderBy(t => t.TotalRank).ToList();
        }

        private SkillWisePlayer TakePlayer(int playerIndex, List<SkillWisePlayer> orderedPlayers, List<SkillWisePlayer> originalPlayers)
        {
            var player = orderedPlayers[playerIndex];
            Log($"Using plauers {player.Name}");
            orderedPlayers.RemoveAt(playerIndex);
            originalPlayers.Remove(player);
            return player;
        }

        private List<SkillWisePlayer> OrderByAndShuffleSequence(List<SkillWisePlayer> players, Func<SkillWisePlayer, double> orderBy)
        {
            var random = new Random();
            var orderedPlayers = players.OrderBy(orderBy).ThenBy((p) => p.Rank).ThenBy((p) => random.Next(0, 10));
            return orderedPlayers.ToList();
        }

        private static void Log(string logMsg)
        {
            if (!GlobalParameters.IsLogEnabled) return;
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine(logMsg);
            Console.ForegroundColor = ConsoleColor.White;
        }

    }
}
