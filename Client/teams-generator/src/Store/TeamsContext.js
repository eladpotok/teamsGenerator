import React, { useState } from "react";

export const TeamsContext = React.createContext({
    teams: {},
    setTeams: (teams) => {},
    setResultAsText: (text) =>{},
    resultAsText: {},
    movePlayer: (fromTeam, toTeam, player) => {},
    removePlayer: (fromTeam, player) => {},

});

export const TeamsContextProvider = (props) => {
    const [teams , setTeams ] = useState(null);
    const [resultAsText, setResultAsText] = useState(null)
    
    function updateTeams(teams) {
        setTeams(teams)
    }

    function movePlayer(fromTeam, toTeam, player) {
        const playersFrom = fromTeam.players.filter(t=> t.name != player.name)
        const playersTo = [...(toTeam.players), player]
        const updatedTeams = []
        teams.forEach(team => {
            if(team.teamId == fromTeam.teamId) {
                team.players = playersFrom
            }
            if(team.teamId == toTeam.teamId) {
                team.players = playersTo
            }
            updatedTeams.push(team)
        });
        updateTeams(updatedTeams);
    }

    function removePlayer(fromTeam, player) {
        const playersFrom = fromTeam.players.filter(t=> t.name != player.name)
        const updatedTeams = []
        teams.forEach(team => {
            if(team.teamId == fromTeam.teamId) {
                team.players = playersFrom
            }
            updatedTeams.push(team)
        });
        updateTeams(updatedTeams);
    }

    

    return <TeamsContext.Provider value={{
        teams: teams,
        setTeams: updateTeams,
        movePlayer: movePlayer,
        removePlayer: removePlayer,
        setResultAsText: setResultAsText,
        resultAsText: resultAsText
        
    }}>
        {props.children}    
    </TeamsContext.Provider>
};

