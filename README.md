# teamsGenerator
This repo is about genberator of teams in soccer (or other sports) with a multiple open source algorithms.


# Algorithms Explanation:

## Back And Forth Algorithm:
Set randomly an order of the teams. The first team pick a player, the second team pick the next player, the third team pick two players.
After this round, it goes back. After the third team pick two players, then the second team pick a player, and then the first team pick two players. and so on.
The team picks a player according to the rank order.
Here is an example of the algorithm selection:

![BackToForthAlgo-Explanation](https://user-images.githubusercontent.com/32292032/227537484-b3272fe0-9454-491f-8dba-d8332f3b43c8.png)


## Skill Wise Algorithm:
I believe that variety of teams should be picked by uniform distrubtion according to skills.
Here we define player by 5 skills: **Leadership** (field domination, increase the moral), **Attack** (finishing, key-pass), **Defence** (close to the goalkeeper, physics, aggression), **Stamina** (fit, pace, speed) and **Passing** (passing accuracy, playmaker).

The teams are ordered randomly, and then the algo picks randomly one of the skills above.
Now we pick top 3 players with the highest rank in the specific skill, and spread them in each team.

Note:
While picking 3 players ordered by skill rank, it goes like this:
1. We pick the highest skill rank.
2. If there are at least two players with the same skill rank, we pick the one with the higher avarage rank (avarage rank means avarage of all other skills rank)
3. If there are at least two players still, we pick randomly.

After this step. we have 3 teams, and each team contain one player according to the random skill.
Now we pick a new skill (of the rest skills), and put the players in the same way.
But now, the order of the picking teams is not random, but ordered by the team total rank (calculated by the summary of the players rank in the team).

