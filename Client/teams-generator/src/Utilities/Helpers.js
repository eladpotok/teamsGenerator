import dayjs from 'dayjs';


export function getTextResult(teams, dateTime) {

    const weekdays = [
               "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"
             ]

    
    let textToCopy =  `*${weekdays[dateTime.day()]} ${dateTime.format('DD/MM/YYYY')}*\n`
    let count = 1

    teams.forEach(team => {
        const teamColor = team.color
        textToCopy += `${team.teamSymbol}Team: ${count} | Color: ${teamColor}\n`

        team.players.forEach(player => {

            let playerDetail = `⚽ ${player.name}\n`
            textToCopy += playerDetail
        })

        textToCopy += "--------------------------------\n"
        count++
    });

    return textToCopy

}

export function writeFileHandler(players, algo)  {
    const playersToSave = players.map (  (player) => {
        const {isArrived, ...restFieldsOfPlayer} = player
        return restFieldsOfPlayer

    }  )
    
    const objectToSave = { players, algoKey: algo.algoKey  }

    const jsonString = `data:text/json;chatset=utf-8,${encodeURIComponent(
        JSON.stringify(objectToSave)
      )}`;
      const link = document.createElement("a");
      link.href = jsonString;
      link.download = "players.json";
  
      link.click();
  
}
