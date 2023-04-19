import React, { useState } from "react";

export const TeamsContext = React.createContext({
    teams: {},
    setTeams: (teams) => {},
    setResultAsText: (text) =>{},
    resultAsText: {},
    movePlayer: (fromTeam, toTeam, player) => {},
    removePlayer: (fromTeam, player) => {},
    changeShirtColor: (teamIdFrom, teamIdTo) => {}

});

export const TeamsContextProvider = (props) => {
    const [teams , setTeams ] = useState(null);
    const [resultAsText, setResultAsText] = useState(null)
    
    function updateTeams(teams) {
        setTeams(teams)
    }

    function movePlayer(fromTeam, toTeam, player) {
        const playersFrom = fromTeam.players.filter(t=> t.name !== player.name)
        const playersTo = [...(toTeam.players), player]
        const updatedTeams = []
        teams.forEach(team => {
            if(team.teamId === fromTeam.teamId) {
                team.players = playersFrom
            }
            if(team.teamId === toTeam.teamId) {
                team.players = playersTo
            }
            updatedTeams.push(team)
        });
        updateTeams(updatedTeams);
    }

    function removePlayer(fromTeam, player) {
        const playersFrom = fromTeam.players.filter(t=> t.name !== player.name)
        const updatedTeams = []
        teams.forEach(team => {
            if(team.teamId === fromTeam.teamId) {
                team.players = playersFrom
            }
            updatedTeams.push(team)
        });
        updateTeams(updatedTeams);
    }

    function changeShirtColor(teamIdFrom, teamIdTo){
        const teamTo = teams.filter(t=> t.teamId === teamIdTo)[0]
        const teamFrom = teams.filter(t=> t.teamId === teamIdFrom)[0]

        const shirtsColorFrom = {teamSymbol: teamFrom.teamSymbol, color: teamFrom.color }
        const shirtsColorTo = {teamSymbol: teamTo.teamSymbol, color: teamTo.color }
        const updatedTeams = []

        teams.forEach(team => {
            if(team.teamId === teamIdFrom) {
                team.teamSymbol = shirtsColorTo.teamSymbol
                team.color = shirtsColorTo.color
            }
            if(team.teamId === teamIdTo) {
                team.teamSymbol = shirtsColorFrom.teamSymbol
                team.color = shirtsColorFrom.color
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
        changeShirtColor,
        setResultAsText: setResultAsText,
        resultAsText: resultAsText
        
    }}>
        {props.children}    
    </TeamsContext.Provider>
};

