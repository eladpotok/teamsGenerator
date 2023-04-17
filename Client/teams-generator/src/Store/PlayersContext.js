import React, { useState } from "react";

export const PlayersContext = React.createContext({
    players: [],
    setPlayers: (players) => {},
    editPlayer: (player) => {}

});

export const PlayersContextProvider = (props) => {

    const [players, setPlayers] = useState([])

    function editPlayer(playerToEdit) {
        const playersExcludingPlayerToEdit = players.filter( player => player.key != playerToEdit.key)
        playersExcludingPlayerToEdit.push(playerToEdit)
        setPlayers(playersExcludingPlayerToEdit)
    }
    
    return <PlayersContext.Provider value={{
        players,
        setPlayers,
        editPlayer
        
    }}>
        {props.children}    
    </PlayersContext.Provider>
};

