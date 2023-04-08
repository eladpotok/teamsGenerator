export async function getInitialAlgoConfig() {
    const resposne = await fetch("https://localhost:7236/Home")
    return resposne.json()
}

export async function getPlayersPropertiesByAlgo(algoTypeIndex) {
    const resposne = await fetch(`https://localhost:7236/PlayersProperties?algoKey=${algoTypeIndex}`)
    return resposne.json()

}


export async function getTeams(algoType, players, config){
    const resposne = await fetch(`https://localhost:7236/Teams?algoKey=${algoType}`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ config: config, players: players}),
    })
    return resposne.json()
}


export async function getResultString(teams){
    const resposne = await fetch(`https://localhost:7236/Teams/PostResultString`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ teams}),
    })
    return resposne.json()
}

