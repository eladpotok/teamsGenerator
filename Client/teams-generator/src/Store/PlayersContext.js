import React, { useState } from "react";

export const PlayersContext = React.createContext({
    players: [],
    setPlayers: (players) => {}

});

export const PlayersContextProvider = (props) => {

    const [players, setPlayers] = useState([])

    return <PlayersContext.Provider value={{
        players,
        setPlayers
        
    }}>
        {props.children}    
    </PlayersContext.Provider>
};

