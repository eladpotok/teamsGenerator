export async function getInitialConfig() {
    return {
        config: {
            numberOfTeams: 3,
            shirtsColors: [
                "red", "orange", "purple", "blue", "blue", "green"
            ]
        },
        algos: [
            {
                algoName: "skillwise",
                description: "this is skillwise algo",
                displayName: "SkillWise",
                algoKey: 0,
                playerProperties: [
                    {
                        name: "Name",
                        type: "text",
                        showInClient: true
                    },
                    {
                        name: "Attack",
                        type: "number",
                        showInClient: true
                    },
                    {
                        name: "Defence",
                        type: "number",
                        showInClient: true
                    },
                    {
                        name: "Stamina",
                        type: "number",
                        showInClient: true
                    },
                    {
                        name: "Passing",
                        type: "number",
                        showInClient: true
                    },
                    {
                        name: "Leadership",
                        type: "number",
                        showInClient: true
                    }
                ]
            },
            {
                algoName: "BackToForth",
                description: "this is back to forth algo",
                displayName: "Back To Forth",
                algoKey: 1,
                playerProperties: [
                    {
                        name: "Name",
                        type: "text",
                        showInClient: true
                    },
                    {
                        name: "Rank",
                        type: "number",
                        showInClient: true
                    }
                ]
            }
        ]
    }
}


export async function getTeams(players, config){
    console.log('players1', players)
    return {
        teams: [
            {
                teamSymbol: '🟥',
                color: 'Red',
                teamId: 1,
                teamName: '1',
                players: [
                    ...players.slice(0, 5).map(p => {return { name: p.Name  }})
                ]
            },
            {
                teamSymbol: '🟩',
                color: 'Green',
                teamId: 2,
                teamName: '2',
                players: [
                    ...players.slice(5, 10).map(p => {return { name: p.Name  }})
                ]
            },
            {
                teamSymbol: '🟨',
                color: 'Yellow',
                teamId: 3,
                teamName: '3',
                players: [
                    ...players.slice(10, 15).map(p => {return { name: p.Name  }})
                ]
            }
            

        ]
    }
}


export async function getResultString(teams){
    const resposne = await fetch(`https://localhost:7236/Teams/PostResultString`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ teams}),
    })
    return resposne.json()
}

