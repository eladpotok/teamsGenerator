import React, { useEffect, useState } from "react";

export const PlayersContext = React.createContext({
    players: [],
    setPlayers: (players) => {},
    editPlayer: (player) => {},
    setPlayerArrived: (player, isArrived) => {},
    setAllPlayerArrived: (areArrived) => {}

});

export const PlayersContextProvider = (props) => {

    const [players, setPlayers] = useState([])

    const arrivedPlayers = players.filter(player => player.isArrived)

    function saveToStorage(players){
        localStorage.setItem('players', JSON.stringify(players))
    }

    function savePlayers(players) {
        setPlayers(players)
        saveToStorage(players)
    }

    function editPlayer(playerToEdit) {
        const playersExcludingPlayerToEdit = players.filter( player => player.key !== playerToEdit.key)
        playersExcludingPlayerToEdit.push(playerToEdit)
        savePlayers(playersExcludingPlayerToEdit)
    }

    function setPlayerArrived(playerToEdit, isArrived) {
        const newPlayers = players.map(player => {
            if(player.key === playerToEdit.key) {
                player.isArrived = isArrived
            }
            return player
        }
        );

        savePlayers(newPlayers)
    }

    function setAllPlayerArrived(isArrived) {
        const newPlayers = players.map(player => {
            player['isArrived'] = isArrived
            return player
        }
        );

        savePlayers(newPlayers)
    }

    useEffect(() => { 
        (async () => { 
            const playersInStorage = localStorage.getItem('players')
            if (playersInStorage) {
                setPlayers(JSON.parse(playersInStorage))
            }
        })() 
      },[])
    
    return <PlayersContext.Provider value={{
        players,
        arrivedPlayers,
        setPlayers: savePlayers,
        editPlayer,
        setPlayerArrived,
        setAllPlayerArrived
        
    }}>
        {props.children}    
    </PlayersContext.Provider>
};

