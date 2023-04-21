
const realService = 'https://teamsgeneratorwebapi20230420202750.azurewebsites.net'
const localEnv = "https://localhost:7236"

export async function getInitialConfig() {
    const resposne = await fetch(`${realService}/Home`)
    return resposne.json()
}

export async function getTeams(players, config){
    console.log('config',config)
    const resposne = await fetch(`${realService}/Teams?algoKey=${config.algo.algoKey}`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ config: config, players: players}),
    })
    return resposne.json()
}


export async function getResultString(teams){
    const resposne = await fetch(`${realService}/Teams/PostResultString`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ teams}),
    })
    return resposne.json()
}

